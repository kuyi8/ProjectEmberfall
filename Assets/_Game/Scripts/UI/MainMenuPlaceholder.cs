using Emberfall.Application.Flow;
using Emberfall.Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Emberfall.UI
{
    /// <summary>
    /// Temporary milestone menu. Replace with production UI during the UI milestone.
    /// </summary>
    public sealed class MainMenuPlaceholder : MonoBehaviour
    {
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _guideStyle;
        private InputAction _startAction;
        private InputAction _guideAction;
        private bool _showGuide = true;
        private bool _showNetworkPanel;
        private string _networkAddress = "127.0.0.1";
        private string _networkPort = "7777";

        private void OnEnable()
        {
            _startAction = new InputAction("Start", InputActionType.Button);
            _startAction.AddBinding("<Keyboard>/enter");
            _startAction.AddBinding("<Keyboard>/numpadEnter");
            _startAction.AddBinding("<Gamepad>/buttonSouth");
            _guideAction = new InputAction("Guide", InputActionType.Button);
            _guideAction.AddBinding("<Keyboard>/f1");
            _guideAction.AddBinding("<Gamepad>/select");
            _startAction.Enable();
            _guideAction.Enable();
        }

        private void OnDisable()
        {
            _startAction?.Dispose();
            _guideAction?.Dispose();
        }

        private void Update()
        {
            if (_startAction?.WasPressedThisFrame() == true)
            {
                ContinueRoute();
            }

            if (_guideAction?.WasPressedThisFrame() == true)
            {
                _showGuide = !_showGuide;
            }
        }

        private void OnGUI()
        {
            _titleStyle ??= CreateStyle(30, FontStyle.Bold, Color.white);
            _bodyStyle ??= CreateStyle(16, FontStyle.Normal, new Color(0.75f, 0.82f, 0.86f));
            _guideStyle ??= CreateStyle(15, FontStyle.Normal, new Color(0.88f, 0.91f, 0.93f));

            const float width = 620f;
            Rect area = new Rect((Screen.width - width) * 0.5f, Mathf.Max(24f, Screen.height * 0.06f), width, 860f);

            GUILayout.BeginArea(area);
            GUILayout.Label("PROJECT EMBERFALL / 烬落计划", _titleStyle);
            GUILayout.Space(12f);
            GUILayout.Label($"M5c 内容密度补齐 / {UnityEngine.Application.version}", _bodyStyle);
            GUILayout.Label("营地任务 • 封印探索 • 组合战斗 • 双人合作 • 守墓人结算", _bodyStyle);
            GUILayout.Space(24f);
            if (GUILayout.Button("继续游戏（Enter）", GUILayout.Height(46f)))
            {
                ContinueRoute();
            }

            GUILayout.Space(10f);
            if (GUILayout.Button("开始新游戏", GUILayout.Height(42f)))
            {
                StartNewRoute();
            }

            GUILayout.Space(10f);
            if (GUILayout.Button("进入战斗训练场", GUILayout.Height(38f)))
            {
                SessionRuntime.Current.StartOffline();
                SceneManager.LoadScene("90_CombatGym", LoadSceneMode.Single);
            }

            GUILayout.Space(10f);
            if (GUILayout.Button("M5 联机会话验证（Direct / LAN）", GUILayout.Height(38f)))
            {
                _showNetworkPanel = !_showNetworkPanel;
            }

            if (_showNetworkPanel)
            {
                DrawNetworkPanel();
            }

            GUILayout.Space(10f);
            GUILayout.Label("F1：显示 / 隐藏操作指南", _bodyStyle);
            if (_showGuide)
            {
                GUILayout.Space(18f);
                GUILayout.Label(
                    "键盘与鼠标操作\n\n" +
                    "W / A / S / D　移动\n" +
                    "左 Shift　　　 冲刺\n" +
                    "E　　　　　　 交互 / 处决 / 激活检查点\n" +
                    "鼠标移动　　 调整镜头\n" +
                    "鼠标左键　　 轻攻击 / 连击\n" +
                    "按住鼠标右键 蓄力重击\n" +
                    "F　　　　　　 投掷飞刀（有冷却）\n" +
                    "Space　　　　 闪避\n" +
                    "鼠标中键　　 锁定 / 解除锁定\n" +
                    "鼠标滚轮　　 切换锁定目标\n" +
                    "Esc　　　　　 返回主菜单\n" +
                    "F1　　　　　　显示 / 隐藏指南",
                    _guideStyle);
            }
            GUILayout.EndArea();
        }

        private static void ContinueRoute()
        {
            SessionRuntime.Current.StartOffline();
            M2LaunchIntent.RequestContinue();
            SceneManager.LoadScene("10_EmberValley", LoadSceneMode.Single);
        }

        private static void StartNewRoute()
        {
            SessionRuntime.Current.StartOffline();
            M2LaunchIntent.RequestNewGame();
            SceneManager.LoadScene("10_EmberValley", LoadSceneMode.Single);
        }

        private void DrawNetworkPanel()
        {
            ISessionService session = SessionRuntime.Current;
            SessionSnapshot snapshot = session.Snapshot;

            GUILayout.Space(12f);
            GUILayout.Label("M5 Direct/LAN：可进入 Network Gym 或正式余烬山谷共享封印段。", _guideStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("主机 IP", _bodyStyle, GUILayout.Width(90f));
            _networkAddress = GUILayout.TextField(_networkAddress, GUILayout.Width(250f));
            GUILayout.Label("端口", _bodyStyle, GUILayout.Width(55f));
            _networkPort = GUILayout.TextField(_networkPort, GUILayout.Width(100f));
            GUILayout.EndHorizontal();

            bool validPort = ushort.TryParse(_networkPort, out ushort port) && port >= 1024;
            bool canStart = snapshot.State == SessionConnectionState.Offline ||
                            snapshot.State == SessionConnectionState.Failed;
            GUILayout.BeginHorizontal();
            GUI.enabled = validPort && canStart;
            if (GUILayout.Button("创建主机", GUILayout.Height(34f))) session.StartHost(port);
            if (GUILayout.Button("加入主机", GUILayout.Height(34f))) session.StartClient(_networkAddress, port);
            GUI.enabled = snapshot.State != SessionConnectionState.Offline;
            if (GUILayout.Button("结束会话", GUILayout.Height(34f))) session.Shutdown();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            snapshot = session.Snapshot;
            GUI.enabled = snapshot.Mode == SessionMode.Host &&
                          snapshot.ConnectedPlayers == 2 &&
                          snapshot.State == SessionConnectionState.Listening;
            if (GUILayout.Button("主机开始 Network Gym", GUILayout.Height(36f))) session.TryStartNetworkGym();
            if (GUILayout.Button("主机开始共享余烬山谷（0.7.10）", GUILayout.Height(36f)))
                session.TryStartNetworkEmberValley();
            GUI.enabled = true;

            snapshot = session.Snapshot;
            GUILayout.Label(
                $"状态：{snapshot.Mode} / {snapshot.State}　玩家：{snapshot.ConnectedPlayers}/2\n" +
                $"握手：{session.Compatibility}\n{snapshot.Message}",
                _guideStyle);
        }

        private static GUIStyle CreateStyle(int fontSize, FontStyle fontStyle, Color color)
        {
            return new GUIStyle(GUI.skin.label)
            {
                font = RuntimeGuiFont.Chinese,
                alignment = TextAnchor.MiddleCenter,
                fontSize = fontSize,
                fontStyle = fontStyle,
                normal = { textColor = color }
            };
        }
    }
}
