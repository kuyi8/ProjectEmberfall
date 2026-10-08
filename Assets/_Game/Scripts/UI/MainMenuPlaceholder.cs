using Emberfall.Application.Flow;
using Emberfall.Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Emberfall.UI
{
    /// <summary>
    /// Presentation menu: direct/LAN authority remains in ISessionService.
    /// </summary>
    public sealed class MainMenuPlaceholder : MonoBehaviour
    {
        [SerializeField] private bool _useSceneBackdrop;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _guideStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _fieldStyle;
        private Texture2D _backdropFade;
        private InputAction _startAction;
        private InputAction _guideAction;
        private bool _showGuide;
        private int _selection;
        private bool _showNetworkPanel;
        private string _networkAddress = "127.0.0.1";
        private string _networkPort = "7777";
        public bool IsNetworkPanelVisible => _showNetworkPanel;
        public void SetNetworkPanelVisible(bool visible) => _showNetworkPanel=visible;
        public int KeyboardSelection => _selection;
        private static readonly string[] MenuLabels={"继续旅程","开始新旅程","战斗训练场","双人合作 · Direct / LAN"};

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
            if(_backdropFade!=null) Destroy(_backdropFade);
            _backdropFade=null;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (!_showNetworkPanel && keyboard != null)
            {
                if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) _selection = (_selection+1)%4;
                if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) _selection = (_selection+3)%4;
            }
            if (_startAction?.WasPressedThisFrame() == true)
            {
                // Enter inside an IP/port editor must not unexpectedly start offline and close the room.
                if (!_showNetworkPanel) ActivateSelection(_selection);
            }

            if (_guideAction?.WasPressedThisFrame() == true)
            {
                _showGuide = !_showGuide;
            }
        }

        private void OnGUI()
        {
            _titleStyle ??= CreateStyle(52, FontStyle.Bold, EmberfallGuiTheme.Text);
            _bodyStyle ??= CreateStyle(16, FontStyle.Normal, new Color(0.75f, 0.82f, 0.86f));
            _guideStyle ??= CreateStyle(15, FontStyle.Normal, new Color(0.88f, 0.91f, 0.93f));
            _buttonStyle ??= EmberfallGuiTheme.ButtonStyle();
            using (EmberfallGuiTheme.Canvas())
            {
                float width=EmberfallGuiTheme.Width, height=EmberfallGuiTheme.Height;
                EmberfallGuiTheme.Fill(new Rect(0,0,width,height),new Color(EmberfallGuiTheme.Ink.r,
                    EmberfallGuiTheme.Ink.g,EmberfallGuiTheme.Ink.b,_useSceneBackdrop ? .12f : 1f));
                if(_useSceneBackdrop)
                {
                    // Readable left-side UI, with the existing scene visible on the right.
                    EmberfallGuiTheme.Fill(new Rect(0,0,width*.39f,height),new Color(.025f,.075f,.085f,.96f));
                    if(_backdropFade==null)
                    {
                        _backdropFade=new Texture2D(64,1,TextureFormat.RGBA32,false);
                        _backdropFade.wrapMode=TextureWrapMode.Clamp; _backdropFade.filterMode=FilterMode.Bilinear;
                        for(int i=0;i<64;i++) _backdropFade.SetPixel(i,0,new Color(.025f,.075f,.085f,.96f*(1-i/63f)));
                        _backdropFade.Apply(false,true);
                    }
                    GUI.DrawTexture(new Rect(width*.39f,0,width*.19f,height),_backdropFade);
                }
                // Native geometric identity, not an imported bitmap or a pretend gameplay screenshot.
                for(int i=0;i<(_useSceneBackdrop ? 0 : 9);i++)
                {
                    float size=380-i*34;
                    var previous=GUI.matrix;
                    GUIUtility.RotateAroundPivot(45,new Vector2(width*.74f,height*.44f));
                    EmberfallGuiTheme.Panel(new Rect(width*.74f-size*.5f,height*.44f-size*.5f,size,size),.12f,
                        i==8 ? EmberfallGuiTheme.Ember : EmberfallGuiTheme.Line);
                    GUI.matrix=previous;
                }
                GUI.Label(new Rect(80,90,620,36),"PROJECT EMBERFALL",_bodyStyle);
                GUI.Label(new Rect(76,128,650,76),"余烬 · 雾谷试炼",_titleStyle);
                EmberfallGuiTheme.Fill(new Rect(80,225,64,3),EmberfallGuiTheme.Ember);
                GUI.Label(new Rect(80,245,480,52),"读懂敌人的预兆，击破封印的守卫。\n在遗迹中选择属于你的余烬祝福。",_guideStyle);
                // Our arrow/Enter selection owns submit outside the text-entry panel; avoid IMGUI Tab submitting a second route.
                if(!_showNetworkPanel) GUIUtility.keyboardControl=0;
                for(int i=0;i<MenuLabels.Length;i++)
                {
                    GUI.SetNextControlName("EmberfallMenu"+i);
                    if(EmberfallGuiTheme.Button(new Rect(80,330+i*64,350,52),
                        (_selection==i ? "›  " : "   ")+MenuLabels[i],_buttonStyle,_selection==i)) ActivateSelection(i);
                }
                GUI.Label(new Rect(80,height-78,560,24),"↑ ↓ / W S 选择 · Enter 确认 · F1 操作指南",_guideStyle);
                GUI.Label(new Rect(width-220,height-54,180,24),$"WINDOWS · {UnityEngine.Application.version}",_bodyStyle);
                if(_showNetworkPanel)
                {
                    Rect panel=new Rect(width-670,100,590,580);
                    EmberfallGuiTheme.Panel(panel,.98f,EmberfallGuiTheme.Interaction);
                    Color previousBackground=GUI.backgroundColor;
                    GUI.backgroundColor=new Color(.075f,.18f,.19f);
                    GUILayout.BeginArea(new Rect(panel.x+24,panel.y+24,panel.width-48,panel.height-48));
                    if(GUILayout.Button("‹ 返回旅程菜单",_buttonStyle,GUILayout.Height(40))) _showNetworkPanel=false;
                    DrawNetworkPanel(); GUILayout.EndArea();
                    GUI.backgroundColor=previousBackground;
                }
                else if(_showGuide)
                {
                    Rect panel=new Rect(width-590,300,500,360);
                    EmberfallGuiTheme.Panel(panel,.97f,EmberfallGuiTheme.Interaction);
                    GUI.Label(new Rect(panel.x+24,panel.y+18,panel.width-48,30),"键盘与鼠标操作",_bodyStyle);
                    GUI.Label(new Rect(panel.x+24,panel.y+64,panel.width-48,276),
                        "WASD 移动 · Shift 冲刺 · 鼠标转动镜头\n\n左键 轻击/连击 · 右键 蓄力重击\nQ 防御/精准防御 · Space 闪避\nF 飞刀 · V 横扫 · R 治疗（可被打断）\nE 交互/近身处决/激活休整点\n\n中键 锁定/解除 · 滚轮 切换目标\nEsc 暂停 · F1 指南 · F2 录制输入\n\n联机不暂停Server；横扫/处决/反击限单人。",_guideStyle);
                }
            }
        }

        private void ActivateSelection(int selection)
        {
            _selection=selection;
            if(selection==0) ContinueRoute();
            else if(selection==1) StartNewRoute();
            else if(selection==2)
            {
                SessionRuntime.Current.StartOffline();
                SceneManager.LoadScene("90_CombatGym",LoadSceneMode.Single);
            }
            else _showNetworkPanel=!_showNetworkPanel;
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
            _fieldStyle ??= new GUIStyle(GUI.skin.textField) {font=RuntimeGuiFont.Chinese,fontSize=16};
            _fieldStyle.normal.textColor=_fieldStyle.focused.textColor=_fieldStyle.hover.textColor=EmberfallGuiTheme.Text;
            Color previousBackground=GUI.backgroundColor;
            GUI.backgroundColor=new Color(.075f,.18f,.19f);
            ISessionService session = SessionRuntime.Current;
            SessionSnapshot snapshot = session.Snapshot;

            GUILayout.Space(12f);
            GUILayout.Label("双人合作 · 同版本局域网直连", _guideStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("主机 IP", _bodyStyle, GUILayout.Width(90f));
            _networkAddress = GUILayout.TextField(_networkAddress,_fieldStyle, GUILayout.Width(230f));
            GUILayout.Label("端口", _bodyStyle, GUILayout.Width(55f));
            _networkPort = GUILayout.TextField(_networkPort,_fieldStyle, GUILayout.Width(90f));
            GUILayout.EndHorizontal();

            bool validPort = ushort.TryParse(_networkPort, out ushort port) && port >= 1024;
            bool canStart = snapshot.State == SessionConnectionState.Offline ||
                            snapshot.State == SessionConnectionState.Failed;
            GUILayout.BeginHorizontal();
            GUI.enabled = validPort && canStart;
            if (GUILayout.Button("创建主机",_buttonStyle, GUILayout.Height(34f))) session.StartHost(port);
            if (GUILayout.Button("加入主机",_buttonStyle, GUILayout.Height(34f))) session.StartClient(_networkAddress, port);
            GUI.enabled = snapshot.State != SessionConnectionState.Offline;
            if (GUILayout.Button("结束会话",_buttonStyle, GUILayout.Height(34f))) session.Shutdown();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            snapshot = session.Snapshot;
            GUI.enabled = snapshot.Mode == SessionMode.Host &&
                          snapshot.ConnectedPlayers == 2 &&
                          snapshot.State == SessionConnectionState.Listening;
            if (GUILayout.Button("主机开始 Network Gym",_buttonStyle, GUILayout.Height(36f))) session.TryStartNetworkGym();
            if (GUILayout.Button("主机开始共享余烬山谷",_buttonStyle, GUILayout.Height(36f)))
                session.TryStartNetworkEmberValley();
            GUI.enabled = true;

            snapshot = session.Snapshot;
            GUILayout.Label(
                $"状态：{snapshot.Mode} / {snapshot.State}　玩家：{snapshot.ConnectedPlayers}/2\n" +
                $"握手：{session.Compatibility}\n{snapshot.Message}",
                _guideStyle);
            GUI.backgroundColor=previousBackground;
        }

        private static GUIStyle CreateStyle(int fontSize, FontStyle fontStyle, Color color)
        {
            return new GUIStyle(GUI.skin.label)
            {
                font = RuntimeGuiFont.Chinese,
                alignment = TextAnchor.MiddleLeft,
                fontSize = fontSize,
                fontStyle = fontStyle,
                wordWrap = true,
                normal = { textColor = color }
            };
        }
    }
}
