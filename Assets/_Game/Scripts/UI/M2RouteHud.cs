using Emberfall.Application.Flow;
using Emberfall.AI.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Interaction;
using Emberfall.Gameplay.Movement;
using Emberfall.Gameplay.Targeting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.UI
{
    public sealed class M2RouteHud : MonoBehaviour
    {
        [SerializeField] private M2RouteFlowController _flow;
        [SerializeField] private PlayerCombatActor _player;
        [SerializeField] private PlayerInteractor _interactor;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private LockOnTargeting _targeting;
        [SerializeField] private ThirdPersonCameraRig _cameraRig;

        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _objectiveStyle;
        private GUIStyle _centerStyle;
        private GUIStyle _threatArrowStyle;
        private bool _paused;
        private bool _showGuide = true;
        private bool _completionPresented;
        private CombatTarget[] _combatTargets = System.Array.Empty<CombatTarget>();
        private string _barrierFeedback;
        private float _barrierFeedbackUntil;
        private bool[] _executionWasReady;
        private AudioSource _executionAudio;
        private AudioClip _executionTone;
        private bool _executionTutorialShown;
        private float _executionTutorialUntil;

        public bool IsPaused => _paused;
        public bool IsGuideVisible => _showGuide;
        public bool IsCompletionPresented => _completionPresented;
        public int OffscreenThreatCount { get; private set; }
        public bool ShouldShowBossHud => _flow?.IsWardenEncounterActive == true &&
            _flow.Warden != null && _flow.Warden.IsAvailable;

        private void Start()
        {
            _combatTargets = Object.FindObjectsOfType<CombatTarget>();
            _executionWasReady = new bool[_combatTargets.Length];
            _executionAudio = gameObject.AddComponent<AudioSource>();
            _executionAudio.playOnAwake = false;
            _executionTone = ConfirmationTone.Create("ExecutionReady", 780f, 0.14f);
        }

        public void Configure(
            M2RouteFlowController flow,
            PlayerCombatActor player,
            PlayerInteractor interactor,
            PlayerInputReader input,
            LockOnTargeting targeting,
            ThirdPersonCameraRig cameraRig)
        {
            _flow = flow;
            _player = player;
            _interactor = interactor;
            _input = input;
            _targeting = targeting;
            _cameraRig = cameraRig;
        }

        private void Update()
        {
            if (_input != null && _input.ConsumeGuidePressed())
            {
                _showGuide = !_showGuide;
            }

            if (_input != null && _input.ConsumePausePressed() && !_completionPresented)
            {
                SetPaused(!_paused);
            }

            if (!_completionPresented && _flow != null && _flow.IsComplete)
            {
                _completionPresented = true;
                Time.timeScale = 0f;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _cameraRig?.SetLookInputBlocked(true);
            }

            RefreshOffscreenThreatCount();
            for (int i = 0; i < _combatTargets.Length; i++)
            {
                CombatTarget target = _combatTargets[i];
                bool ready = target != null && target.IsAvailable && target is IExecutionTarget execution &&
                    execution.IsExecutionEligible && _player != null &&
                    (target.transform.position - _player.transform.position).sqrMagnitude <= 144f;
                if (ready && !_executionWasReady[i])
                {
                    _executionAudio.PlayOneShot(_executionTone, 0.5f);
                    if (!_executionTutorialShown)
                    {
                        _executionTutorialShown = true;
                        _executionTutorialUntil = Time.time + 8f;
                    }
                }
                _executionWasReady[i] = ready;
            }
        }

        private void OnDestroy()
        {
            if (_executionTone != null) Destroy(_executionTone);
        }

        private void OnEnable() => M2StageBarrier.FeedbackPresented += OnBarrierFeedback;

        private void OnBarrierFeedback(string textId)
        {
            _barrierFeedback = textId;
            _barrierFeedbackUntil = Time.time + 3f;
        }

        private void OnDisable()
        {
            M2StageBarrier.FeedbackPresented -= OnBarrierFeedback;
            Time.timeScale = 1f;
            _cameraRig?.SetLookInputBlocked(false);
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (_flow == null || !_flow.IsInitialized || _player?.Model == null)
            {
                return;
            }

            DrawPlayerPanel();
            DrawQuestPanel();
            DrawBossHud();
            DrawTargetStatus();
            DrawOffscreenThreats();
            DrawInteractionPrompt();
            if (!_paused && !_completionPresented)
            {
                DrawExecutionMarkers();
                DrawActionFeedback();
            }
            if (_showGuide && !_paused && !_completionPresented)
            {
                DrawGuide();
            }

            if (_paused)
            {
                DrawPausePanel();
            }
            else if (_completionPresented)
            {
                DrawCompletionPanel();
            }
        }

        private void DrawPlayerPanel()
        {
            Rect panel = new Rect(18f, 18f, 350f, 228f);
            DrawPanel(panel, 0.88f);
            GUI.Label(new Rect(34f, 28f, 290f, 26f), _flow.RouteTitle, _titleStyle);
            DrawBar(new Rect(34f, 64f, 282f, 15f), _player.Model.Health.Normalized, new Color(0.78f, 0.18f, 0.13f));
            DrawBar(new Rect(34f, 88f, 282f, 11f), _player.Model.Stamina.Normalized, new Color(0.15f, 0.72f, 0.53f));
            DrawBar(new Rect(34f, 105f, 282f, 8f), _player.Model.Posture.Normalized, new Color(0.95f, 0.62f, 0.16f));
            GUI.Label(
                new Rect(34f, 118f, 282f, 24f),
                $"生命 {_player.Model.Health.Current:0}/{_player.Model.Health.Maximum:0}　耐力 {_player.Model.Stamina.Current:0}/{_player.Model.Stamina.Maximum:0}　架势 {_player.Model.Posture.Current:0}",
                _bodyStyle);
            string guardReady = _player.IsGuardCounterReady ? " · 反击就绪" : string.Empty;
            GUI.Label(
                new Rect(34f, 146f, 302f, 21f),
                $"药剂：{_player.Model.HealingFlasks.CurrentCharges}/{_player.Model.HealingFlasks.MaximumCharges}　R 使用" +
                (_player.Model.State == Emberfall.Gameplay.Combat.Domain.CombatState.Heal
                    ? $" · 引导 {_player.Model.StateNormalized:P0}"
                    : string.Empty),
                _bodyStyle);
            string runeDetail = _player.ActiveRuneBlessing switch
            {
                Emberfall.Gameplay.Combat.Domain.RuneBlessing.Ember => "满蓄重击爆发/破势",
                Emberfall.Gameplay.Combat.Domain.RuneBlessing.Guard => "精准防御/闪避后轻击反击",
                _ => "未选择"
            };
            GUI.Label(
                new Rect(34f, 171f, 302f, 21f),
                $"飞刀 {(_player.Model.RangedCooldownRemaining <= 0f ? "就绪" : $"{_player.Model.RangedCooldownRemaining:0.0}s")}" +
                $"　横扫 {(_player.Model.SweepCooldownRemaining <= 0f ? "就绪" : $"{_player.Model.SweepCooldownRemaining:0.0}s")}",
                _bodyStyle);
            GUI.Label(
                new Rect(34f, 196f, 302f, 21f),
                $"符文：{runeDetail}{guardReady}",
                _bodyStyle);
        }

        private void DrawQuestPanel()
        {
            string checklist = _flow.SealConditionChecklist;
            bool showChecklist = !string.IsNullOrEmpty(checklist);
            Rect panel = new Rect(Screen.width - 414f, 18f, 396f, showChecklist ? 202f : 136f);
            DrawPanel(panel, 0.88f);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 10f, panel.width - 32f, 26f), _flow.QuestTitle, _titleStyle);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 44f, panel.width - 32f, 46f), _flow.CurrentObjective, _objectiveStyle);
            if (showChecklist)
            {
                GUI.Label(new Rect(panel.x + 16f, panel.y + 94f, panel.width - 32f, 46f), checklist, _bodyStyle);
                GUI.Label(new Rect(panel.x + 16f, panel.y + 158f, panel.width - 32f, 30f), _flow.LastMessage, _bodyStyle);
            }
            else
            {
                GUI.Label(new Rect(panel.x + 16f, panel.y + 98f, panel.width - 32f, 25f), _flow.LastMessage, _bodyStyle);
            }
        }

        private void DrawBossHud()
        {
            if (!ShouldShowBossHud) return;
            var warden = _flow.Warden;
            Rect panel = new Rect((Screen.width - 620f) * 0.5f, 18f, 620f, 104f);
            DrawPanel(panel, 0.9f);
            GUI.Label(new Rect(panel.x + 18f, panel.y + 7f, panel.width - 36f, 25f),
                $"{_flow.Resolve(warden.Definition.DisplayNameTextId)} · {ResolvePhaseName(warden.Phase)}", _titleStyle);
            DrawBar(new Rect(panel.x + 42f, panel.y + 39f, panel.width - 84f, 14f),
                warden.HealthNormalized, new Color(0.78f, 0.12f, 0.07f));
            DrawBar(new Rect(panel.x + 42f, panel.y + 59f, panel.width - 84f, 7f),
                warden.SecondaryResourceNormalized, new Color(0.12f, 0.62f, 0.9f));
            string hint = warden.State == WardenState.PhaseTransition ? "盾牌崩解 · 转换期间不可受击，观察场地符文" :
                warden.State == WardenState.Recovery ? "收招破绽 · 伤害提升" :
                warden.State == WardenState.GuardBreak ? "架势崩解 · 全力输出" :
                warden.State == WardenState.Windup ? $"预兆：{ResolveAttackName(warden.Brain.CurrentAttack)}" :
                warden.Phase == WardenPhase.PhaseOne ? "正面攻击削减盾架势，绕后或等待收招" :
                "盾已破碎 · 攻击同时削减生命与韧性";
            GUI.Label(new Rect(panel.x + 18f, panel.y + 67f, panel.width - 36f, 22f), hint, _bodyStyle);
        }

        private static string ResolvePhaseName(WardenPhase phase) => phase switch
        {
            WardenPhase.Transition => "盾破转换",
            WardenPhase.PhaseTwo => "第二阶段",
            _ => "第一阶段"
        };

        private static string ResolveAttackName(WardenAttackKind attack) => attack switch
        {
            WardenAttackKind.ShieldBash => "盾击（高架势伤害）",
            WardenAttackKind.Charge => "直线冲锋（方向已锁定）",
            WardenAttackKind.RuneCleave => "扇形符文斩（离开正面扇区）",
            WardenAttackKind.DelayedBlast => "延迟爆炸（离开脚下断环）",
            _ => "二连斩"
        };

        private void DrawInteractionPrompt()
        {
            CombatTarget executionTarget = _player?.ExecutionPromptTarget;
            if (executionTarget != null)
            {
                Rect executePanel = new Rect((Screen.width - 440f) * 0.5f, Screen.height - 104f, 440f, 48f);
                DrawPanel(executePanel, 0.94f);
                Color previous = GUI.color;
                GUI.color = ExecutionColor((IExecutionTarget)executionTarget);
                GUI.Label(executePanel, "E　处决", _centerStyle);

                GUI.color = previous;
                return;
            }

            IInteractable candidate = _interactor?.CurrentCandidate;
            if (candidate == null)
            {
                return;
            }

            string prompt = _flow.Resolve(candidate.PromptTextId);
            Rect panel = new Rect((Screen.width - 440f) * 0.5f, Screen.height - 104f, 440f, 48f);
            DrawPanel(panel, 0.92f);
            GUI.Label(panel, $"E　{prompt}", _centerStyle);
        }

        private void DrawGuide()
        {
            Rect panel = new Rect(18f, Screen.height - 274f, 304f, 210f);
            DrawPanel(panel, 0.82f);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 8f, panel.width - 28f, 25f), "操作提示（F1 隐藏）", _titleStyle);
            GUI.Label(
                new Rect(panel.x + 14f, panel.y + 40f, panel.width - 28f, panel.height - 48f),
                "WASD 移动　Shift 冲刺\nE 交互 / 近身处决　左键轻击\n按住右键蓄力重击\nF 投掷飞刀（有冷却）\nV 大范围横扫（有冷却）\nQ 防御 / 精准防御\nR 治疗药（可被打断）\nSpace 闪避　中键锁定\nF2 输入显示　Esc 暂停",
                _bodyStyle);
        }

        private void DrawTargetStatus()
        {
            if (_targeting == null || !_targeting.IsLocked || Camera.main == null)
            {
                return;
            }

            CombatTarget target = _targeting.CurrentTarget;
            Vector3 screen = Camera.main.WorldToScreenPoint(target.AimPoint.position);
            if (screen.z <= 0f)
            {
                return;
            }

            float x = screen.x;
            float y = Screen.height - screen.y;
            Color previous = GUI.color;
            GUI.color = new Color(1f, 0.58f, 0.16f, 0.95f);
            GUI.DrawTexture(new Rect(x - 26f, y - 26f, 12f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + 14f, y - 26f, 12f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 26f, y + 23f, 12f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + 14f, y + 23f, 12f, 3f), Texture2D.whiteTexture);
            GUI.color = previous;
            DrawBar(new Rect(x - 48f, y - 42f, 96f, 7f), target.HealthNormalized, new Color(0.86f, 0.24f, 0.12f));
            if (target.HasSecondaryResource)
            {
                DrawBar(
                    new Rect(x - 48f, y - 32f, 96f, 5f),
                    target is IExecutionTarget ? 1f - target.SecondaryResourceNormalized : target.SecondaryResourceNormalized,
                    target is IExecutionTarget executable ? ExecutionColor(executable) : new Color(0.2f, 0.68f, 0.92f));
            }
            if (target is IExecutionTarget execution)
            {
                string id = ExecutionRules.ConditionTextId(execution.ExecutionKind,
                    target.HealthNormalized, execution.IsPostureExecutionWindow, execution.IsExecutionClaimed);
                GUI.Label(new Rect(x - 230f, y - 95f, 460f, 28f), Text(id), _centerStyle);
            }
        }

        private string Text(string id) => _flow.Resolve(new ContentId(id));

        private static Color ExecutionColor(IExecutionTarget target)
        {
            if (target.IsExecutionClaimed) return new Color(0.76f, 0.76f, 0.76f);
            if (target.IsPostureExecutionWindow)
                return Color.Lerp(new Color(1f, 0.58f, 0.05f), new Color(1f, 0.92f, 0.4f),
                    0.5f + 0.5f * Mathf.Sin(Time.time * 12f));
            return target.IsExecutionEligible ? new Color(0.95f, 0.38f, 0.4f) : new Color(0.2f, 0.68f, 0.92f);
        }

        private void DrawExecutionMarkers()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            foreach (CombatTarget target in _combatTargets)
            {
                if (target == null || !target.IsAvailable || !(target is IExecutionTarget execution) ||
                    (target.transform.position - _player.transform.position).sqrMagnitude > 144f) continue;
                string marker = ExecutionRules.MarkerTextId(execution.ExecutionKind, target.HealthNormalized,
                    execution.IsPostureExecutionWindow, execution.IsExecutionClaimed,
                    target.SecondaryResourceNormalized);
                if (string.IsNullOrEmpty(marker)) continue;
                Vector3 screen = camera.WorldToScreenPoint(target.AimPoint.position + Vector3.up * 0.7f);
                if (screen.z <= 0f) continue;
                Rect markerRect = new Rect(screen.x - 104f, Screen.height - screen.y - 22f, 208f, 28f);
                DrawPanel(markerRect, 0.75f);
                Color previous = GUI.color;
                GUI.color = ExecutionColor(execution);
                GUI.Label(markerRect, Text(marker), _centerStyle);
                GUI.color = previous;
            }
        }

        private void DrawActionFeedback()
        {
            string feedback = _player.FeedbackUntil > Time.time ? _player.FeedbackTextId : null;
            if (string.IsNullOrEmpty(feedback))
                feedback = _barrierFeedbackUntil > Time.time ? _barrierFeedback : null;
            if (!string.IsNullOrEmpty(feedback))
            {
                Rect panel = new Rect((Screen.width - 620f) * 0.5f, Screen.height - 160f, 620f, 42f);
                DrawPanel(panel, 0.92f);
                GUI.Label(panel, Text(feedback), _centerStyle);
            }
            if (Time.time < _executionTutorialUntil)
                GUI.Label(new Rect((Screen.width - 800f) * 0.5f, Screen.height - 205f, 800f, 35f),
                    Text("text:execution.tutorial"), _centerStyle);
        }

        private void DrawOffscreenThreats()
        {
            Camera gameplayCamera = Camera.main;
            if (gameplayCamera == null || _combatTargets == null) return;

            for (int i = 0; i < _combatTargets.Length; i++)
            {
                CombatTarget target = _combatTargets[i];
                if (target == null || target == _player || !target.IsAvailable || !target.IsThreatening) continue;
                if (!TryGetOffscreenMarker(gameplayCamera, target, out Vector2 marker, out Vector2 direction)) continue;

                float pulse = 0.82f + Mathf.Sin(Time.unscaledTime * 11f) * 0.18f;
                Color previous = GUI.color;
                Matrix4x4 previousMatrix = GUI.matrix;
                GUI.color = new Color(1f, 0.24f, 0.05f, pulse);
                GUIUtility.RotateAroundPivot(
                    Vector2.SignedAngle(new Vector2(0f, -1f), direction),
                    marker);
                GUI.Label(new Rect(marker.x - 24f, marker.y - 28f, 48f, 48f), "▲", _threatArrowStyle);
                GUI.matrix = previousMatrix;
                GUI.Label(new Rect(marker.x - 18f, marker.y - 11f, 36f, 36f), "!", _centerStyle);
                GUI.color = previous;
            }
        }

        private void RefreshOffscreenThreatCount()
        {
            OffscreenThreatCount = 0;
            Camera gameplayCamera = Camera.main;
            if (gameplayCamera == null || _combatTargets == null) return;
            for (int i = 0; i < _combatTargets.Length; i++)
            {
                CombatTarget target = _combatTargets[i];
                if (target == null || target == _player || !target.IsAvailable || !target.IsThreatening) continue;
                if (TryGetOffscreenMarker(gameplayCamera, target, out _, out _)) OffscreenThreatCount++;
            }
        }

        private static bool TryGetOffscreenMarker(
            Camera gameplayCamera,
            CombatTarget target,
            out Vector2 marker,
            out Vector2 direction)
        {
            const float horizontalMargin = 72f;
            const float verticalMargin = 92f;
            Vector3 viewport = gameplayCamera.WorldToViewportPoint(target.AimPoint.position);
            if (viewport.z < 0f)
            {
                viewport.x = 1f - viewport.x;
                viewport.y = 1f - viewport.y;
            }

            bool onScreen = viewport.z > 0f &&
                viewport.x >= horizontalMargin / Screen.width &&
                viewport.x <= 1f - horizontalMargin / Screen.width &&
                viewport.y >= verticalMargin / Screen.height &&
                viewport.y <= 1f - verticalMargin / Screen.height;
            Vector2 raw = new Vector2(viewport.x * Screen.width, (1f - viewport.y) * Screen.height);
            marker = new Vector2(
                Mathf.Clamp(raw.x, horizontalMargin, Screen.width - horizontalMargin),
                Mathf.Clamp(raw.y, verticalMargin, Screen.height - verticalMargin));
            direction = raw - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (direction.sqrMagnitude <= 0.001f) direction = Vector2.up;
            else direction.Normalize();
            return !onScreen;
        }

        private void DrawPausePanel()
        {
            Rect panel = CenteredPanel(420f, 270f);
            DrawPanel(panel, 0.97f);
            GUILayout.BeginArea(new Rect(panel.x + 36f, panel.y + 28f, panel.width - 72f, panel.height - 56f));
            GUILayout.Label("游戏已暂停", _titleStyle);
            GUILayout.Space(24f);
            if (GUILayout.Button("继续游戏", GUILayout.Height(44f)))
            {
                SetPaused(false);
            }

            GUILayout.Space(12f);
            if (GUILayout.Button("保存并返回主菜单", GUILayout.Height(44f)))
            {
                ReturnToMainMenu();
            }
            GUILayout.EndArea();
        }

        private void DrawCompletionPanel()
        {
            Rect panel = CenteredPanel(520f, 370f);
            DrawPanel(panel, 0.98f);
            GUILayout.BeginArea(new Rect(panel.x + 42f, panel.y + 30f, panel.width - 84f, panel.height - 60f));
            GUILayout.Label("余烬封印已稳定", _titleStyle);
            GUILayout.Space(18f);
            GUILayout.Label(
                $"余烬封印任务已完成\n\n用时：{FormatTime(_flow.SessionElapsedSeconds)}\n倒地次数：{_flow.DeathCount}\n" +
                $"最后休整点：{GetCheckpointDisplayName()}\n路线抉择：{_flow.RouteChoiceStatus}\n" +
                $"险径奖励：{(_flow.RiskRouteRewardClaimed ? "已取得（药剂上限 +1）" : "未取得")}\n" +
                $"旧瞭望塔：{(_flow.WatchtowerDiscovered ? "已发现" : "未发现")}",
                _objectiveStyle);
            GUILayout.Space(24f);
            if (GUILayout.Button("返回主菜单", GUILayout.Height(46f)))
            {
                ReturnToMainMenu();
            }
            GUILayout.EndArea();
        }

        private string GetCheckpointDisplayName()
        {
            return _player.ActiveCheckpointId.Value == "checkpoint:courtyard"
                ? "圣所前庭"
                : "林地营地";
        }

        public void SetPaused(bool paused)
        {
            if (_completionPresented && !paused)
            {
                return;
            }

            _paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = paused;
            _cameraRig?.SetLookInputBlocked(paused || _completionPresented);
        }

        private void ReturnToMainMenu()
        {
            _flow?.SaveSessionProgress();
            Time.timeScale = 1f;
            SceneManager.LoadScene("01_MainMenu", LoadSceneMode.Single);
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        private static Rect CenteredPanel(float width, float height) =>
            new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

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
            _centerStyle ??= new GUIStyle(_objectiveStyle)
            {
                fontStyle = FontStyle.Bold
            };
            _threatArrowStyle ??= new GUIStyle(_centerStyle)
            {
                fontSize = 28,
                alignment = TextAnchor.MiddleCenter
            };
        }

        private static void DrawPanel(Rect rect, float alpha)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.015f, 0.025f, 0.035f, alpha);
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
