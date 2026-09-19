using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Movement;
using Emberfall.Gameplay.Targeting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.UI
{
    /// <summary>Development-only M1 combat instrumentation. It is intentionally scene-owned.</summary>
    public sealed class CombatDebugOverlay : MonoBehaviour
    {
        [SerializeField] private PlayerCombatActor _player;
        [SerializeField] private ThirdPersonMotor _motor;
        [SerializeField] private LockOnTargeting _targeting;
        [SerializeField] private Camera _camera;
        [SerializeField] private PlayerInputReader _input;

        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _centerStyle;
        private string _snapshot = string.Empty;
        private float _nextSnapshotTime;
        private bool _showGuide = true;

        public bool IsGuideVisible => _showGuide;

        public void Configure(
            PlayerCombatActor player,
            ThirdPersonMotor motor,
            LockOnTargeting targeting,
            Camera gameplayCamera,
            PlayerInputReader input)
        {
            _player = player;
            _motor = motor;
            _targeting = targeting;
            _camera = gameplayCamera;
            _input = input;
        }

        private void Update()
        {
            if (_input != null && _input.ConsumeGuidePressed())
            {
                _showGuide = !_showGuide;
            }

            if (_input != null && _input.ConsumePausePressed())
            {
                SceneManager.LoadScene("01_MainMenu", LoadSceneMode.Single);
                return;
            }

            if (_player?.Model == null || Time.unscaledTime < _nextSnapshotTime)
            {
                return;
            }

            _nextSnapshotTime = Time.unscaledTime + 0.1f;
            string target = _targeting != null && _targeting.IsLocked
                ? $"已锁定  生命 {_targeting.CurrentTarget.HealthNormalized * 100f:0}%"
                : "未锁定";
            _snapshot =
                $"状态　 {_player.Model.State}\n" +
                $"生命　 {_player.Model.Health.Current:0}/{_player.Model.Health.Maximum:0}\n" +
                $"耐力　 {_player.Model.Stamina.Current:0}/{_player.Model.Stamina.Maximum:0}\n" +
                $"架势　 {_player.Model.Posture.Current:0}/{_player.Model.Posture.Maximum:0}\n" +
                $"飞刀　 {(_player.Model.RangedCooldownRemaining <= 0f ? "Ready" : $"{_player.Model.RangedCooldownRemaining:0.0}s")}\n" +
                $"速度　 {(_motor != null ? _motor.HorizontalSpeed : 0f):0.0} m/s\n" +
                $"目标　 {target}\n" +
                $"事件　 {_player.LastCombatEvent}";
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (_player?.Model == null)
            {
                return;
            }

            DrawPanel(new Rect(18f, 18f, 340f, 250f));
            GUI.Label(new Rect(34f, 28f, 300f, 28f), "M1 战斗遥测", _titleStyle);
            DrawBar(new Rect(34f, 63f, 290f, 14f), _player.Model.Health.Normalized, new Color(0.78f, 0.18f, 0.13f));
            DrawBar(new Rect(34f, 84f, 290f, 10f), _player.Model.Stamina.Normalized, new Color(0.15f, 0.72f, 0.53f));
            DrawBar(new Rect(34f, 100f, 290f, 8f), _player.Model.Posture.Normalized, new Color(0.95f, 0.62f, 0.16f));
            GUI.Label(new Rect(34f, 114f, 300f, 144f), _snapshot, _bodyStyle);

            float controlsWidth = 800f;
            Rect controls = new Rect((Screen.width - controlsWidth) * 0.5f, Screen.height - 58f, controlsWidth, 40f);
            DrawPanel(controls);
            GUI.Label(controls, "WASD 移动 · Shift 冲刺 · E 互动/处决 · 左键连击 · 右键重击 · F 飞刀 · Q 防御 · Space 闪避 · 中键锁定 · F1 指南 · Esc 菜单", _centerStyle);

            if (_showGuide)
            {
                Rect guide = new Rect(Screen.width - 330f, 18f, 312f, 280f);
                DrawPanel(guide);
                GUI.Label(new Rect(guide.x + 16f, guide.y + 10f, guide.width - 32f, 28f), "键盘与鼠标操作", _titleStyle);
                GUI.Label(
                    new Rect(guide.x + 16f, guide.y + 45f, guide.width - 32f, guide.height - 55f),
                    "W / A / S / D　移动\n" +
                    "左 Shift　　　 冲刺\n" +
                    "E　　　　　　 交互 / 处决 / 激活检查点\n" +
                    "鼠标移动　　 调整镜头\n" +
                    "鼠标左键　　 轻攻击 / 连击\n" +
                    "按住鼠标右键 蓄力重击\n" +
                    "F　　　　　　 投掷飞刀（有冷却）\n" +
                    "Q　　　　　　 防御 / 精准防御\n" +
                    "Space　　　　 闪避\n" +
                    "鼠标中键　　 锁定 / 解除\n" +
                    "鼠标滚轮　　 切换目标\n" +
                    "F1　　　　　　显示 / 隐藏指南\n" +
                    "Esc　　　　　 返回主菜单",
                    _bodyStyle);
            }

            if (_targeting != null && _targeting.IsLocked && _camera != null)
            {
                Vector3 screen = _camera.WorldToScreenPoint(_targeting.CurrentTarget.AimPoint.position);
                if (screen.z > 0f)
                {
                    Rect marker = new Rect(screen.x - 14f, Screen.height - screen.y - 14f, 28f, 28f);
                    GUI.Label(marker, "◇", _centerStyle);
                }
            }
        }

        private void EnsureStyles()
        {
            _titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                font = RuntimeGuiFont.Chinese,
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.95f, 0.78f, 0.38f) }
            };
            _bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                font = RuntimeGuiFont.Chinese,
                fontSize = 13,
                normal = { textColor = new Color(0.86f, 0.9f, 0.92f) }
            };
            _centerStyle ??= new GUIStyle(_bodyStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
        }

        private static void DrawPanel(Rect rect)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.015f, 0.025f, 0.035f, 0.88f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawBar(Rect rect, float normalized, Color color)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.08f, 0.09f, 0.1f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(normalized), rect.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
