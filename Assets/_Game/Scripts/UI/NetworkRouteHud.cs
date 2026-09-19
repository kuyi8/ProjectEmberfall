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
        private bool _showGuide = true;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _objectiveStyle;
        private GUIStyle _centerStyle;

        public bool IsConfigured => _player != null && _input != null && _targeting != null;
        public bool IsLockOnConfigured => _targeting != null;
        public bool HasReplicatedObjective => _objective != null;

        public void Configure(NetworkGymPlayer player, PlayerInputReader input, LockOnTargeting targeting)
        {
            _player = player;
            _input = input;
            _targeting = targeting;
        }

        public static string BuildInteractionPrompt(string interactionLabel, bool inRange, float distance)
        {
            if (string.IsNullOrWhiteSpace(interactionLabel)) return string.Empty;
            return inRange
                ? $"E　{interactionLabel}"
                : $"前往目标 · {interactionLabel} · {Mathf.Max(0f, distance):0.0}m";
        }

        private void Awake()
        {
            _player ??= GetComponent<NetworkGymPlayer>();
            _input ??= GetComponent<PlayerInputReader>();
            _targeting ??= GetComponent<LockOnTargeting>();
        }

        private void Update()
        {
            if (_player == null || !_player.IsOwner) return;
            if (_input != null && _input.ConsumeGuidePressed()) _showGuide = !_showGuide;
            if (Time.unscaledTime < _nextWorldRefresh) return;
            _nextWorldRefresh = Time.unscaledTime + 0.35f;
            _objective = FindObjectOfType<NetworkGymWorldObjective>();
            _warden = FindObjectOfType<NetworkWarden>();
        }

        private void OnGUI()
        {
            if (_player == null || !_player.IsSpawned || !_player.IsOwner) return;
            EnsureStyles();
            NetworkHudLayout layout = NetworkHudLayout.Resolve(Screen.width);
            DrawPlayerPanel(layout.Player);
            DrawQuestPanel(layout.Quest);
            DrawBossPanel(layout.Boss);
            DrawLockedTarget();
            DrawInteractionGuidance();
            DrawSessionState();
            if (_showGuide) DrawGuide();
        }

        private void DrawPlayerPanel(Rect panel)
        {
            DrawPanel(panel, 0.9f);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 7f, panel.width - 32f, 28f), "余烬谷 · 双人协作", _titleStyle);
            float barWidth = panel.width - 68f;
            DrawBar(new Rect(panel.x + 16f, panel.y + 46f, barWidth, 15f), _player.Health / _player.MaximumHealth, new Color(0.78f, 0.18f, 0.13f));
            DrawBar(new Rect(panel.x + 16f, panel.y + 70f, barWidth, 11f), _player.Stamina / _player.MaximumStamina, new Color(0.15f, 0.72f, 0.53f));
            DrawBar(new Rect(panel.x + 16f, panel.y + 87f, barWidth, 8f), _player.Posture / _player.MaximumPosture, new Color(0.95f, 0.62f, 0.16f));
            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 101f, panel.width - 32f, 24f),
                $"生命 {_player.Health:0}/{_player.MaximumHealth:0}　耐力 {_player.Stamina:0}/{_player.MaximumStamina:0}　架势 {_player.Posture:0}",
                _bodyStyle);
            string knife = _player.ApproximateRangedCooldownRemaining <= 0f
                ? "飞刀就绪"
                : $"飞刀冷却 {_player.ApproximateRangedCooldownRemaining:0.0}s";
            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 127f, panel.width - 32f, 22f),
                $"药剂 {_player.HealingFlaskCharges}　{knife}　状态 {_player.ReplicatedCombatState}",
                _bodyStyle);
            if (_player.IsDowned)
                GUI.Label(new Rect(panel.x + 16f, panel.y + 150f, panel.width - 32f, 22f), $"已倒地 · 等待救援 {_player.RescueProgress:P0}", _bodyStyle);
        }

        private void DrawQuestPanel(Rect panel)
        {
            DrawPanel(panel, 0.9f);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 8f, panel.width - 32f, 28f), "主线 · 余烬封印", _titleStyle);
            string objective = NetworkLocalizedText.Resolve(
                _objective != null ? _objective.GetObjectiveTextId() : "text:network.quest.syncing");
            GUI.Label(new Rect(panel.x + 16f, panel.y + 39f, panel.width - 32f, 70f), objective, _objectiveStyle);
            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 116f, panel.width - 32f, 22f),
                $"共享余烬：{_player.SharedRewardCount}/1",
                _bodyStyle);
        }

        private void DrawBossPanel(Rect panel)
        {
            if (_warden == null || !_warden.IsAlive) return;
            DrawPanel(panel, 0.92f);
            GUI.Label(new Rect(panel.x + 18f, panel.y + 6f, panel.width - 36f, 27f), "余烬守望者", _titleStyle);
            DrawBar(new Rect(panel.x + 42f, panel.y + 38f, panel.width - 84f, 14f),
                _warden.HealthNormalized, new Color(0.78f, 0.12f, 0.07f));
            DrawBar(new Rect(panel.x + 42f, panel.y + 58f, panel.width - 84f, 7f),
                _warden.PostureNormalized, new Color(0.12f, 0.62f, 0.9f));
            GUI.Label(
                new Rect(panel.x + 18f, panel.y + 67f, panel.width - 36f, 20f),
                $"{ResolveWardenPhase()} · {ResolveWardenState()}",
                _centerStyle);
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

        private void DrawInteractionGuidance()
        {
            if (_objective == null || !_objective.TryGetCurrentInteractionTarget(out Vector3 position))
            {
                DrawInteractionFeedback();
                return;
            }

            Vector3 offset = position - _player.transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            bool inRange = distance <= _objective.PlayerInteractionRange;
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

            string interaction = NetworkLocalizedText.Resolve(_objective.GetInteractionTextId());
            string prompt = BuildInteractionPrompt(interaction, inRange, distance);
            if (string.IsNullOrEmpty(prompt))
            {
                DrawInteractionFeedback();
                return;
            }
            Rect panel = new Rect((Screen.width - 470f) * 0.5f, Screen.height - 112f, 470f, 50f);
            DrawPanel(panel, 0.93f);
            GUI.Label(panel, prompt, _centerStyle);
            DrawInteractionFeedback();
        }

        private void DrawInteractionFeedback()
        {
            if (!_player.HasInteractionFeedback) return;
            Rect rect = new Rect((Screen.width - 460f) * 0.5f, Screen.height - 164f, 460f, 38f);
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
            Rect panel = new Rect((Screen.width - 460f) * 0.5f, (Screen.height - 72f) * 0.5f, 460f, 72f);
            DrawPanel(panel, 0.95f);
            GUI.Label(panel, message, _centerStyle);
        }

        private void DrawGuide()
        {
            Rect panel = new Rect(18f, Screen.height - 236f, 304f, 218f);
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
                normal = { textColor = new Color(0.96f, 0.77f, 0.35f) }
            };
            _bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                font = RuntimeGuiFont.Chinese,
                fontSize = 14,
                normal = { textColor = new Color(0.86f, 0.9f, 0.92f) }
            };
            _objectiveStyle ??= new GUIStyle(_bodyStyle)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            _centerStyle ??= new GUIStyle(_objectiveStyle) { fontStyle = FontStyle.Bold };
        }

        private static void DrawPanel(Rect rect, float alpha)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.015f, 0.025f, 0.035f, alpha);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawBar(Rect rect, float normalized, Color fill)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.035f, 0.045f, 0.055f, 0.96f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 2f,
                Mathf.Max(0f, (rect.width - 4f) * Mathf.Clamp01(normalized)), rect.height - 4f), Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
