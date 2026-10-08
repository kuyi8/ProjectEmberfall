using Emberfall.Application.Flow;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
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
        private GUIStyle _buttonStyle;
        private GUIStyle _completionTitleStyle;
        private GUIStyle _completionHeadingStyle;
        private GUIStyle _completionBodyStyle;
        private GUIStyle _completionNoteStyle;
        private bool _paused;
        private bool _showGuide = false;
        private bool _completionPresented;
        private CombatTarget[] _combatTargets = System.Array.Empty<CombatTarget>();
        private int _authoredTargetCount;
        private string _barrierFeedback;
        private float _barrierFeedbackUntil;
        private bool[] _executionWasReady;
        private AudioSource _executionAudio;
        private AudioClip _executionTone;
        private bool _executionTutorialShown;
        private float _executionTutorialUntil;
        private HudResourcePresentation _resources;
        private string _flaskStatus;
        private string _flaskCount;
        private int _lastFlaskCharges = -1;
        private int _lastFlaskMaximum = -1;
        private SummonerEnemyActor[] _summoners = System.Array.Empty<SummonerEnemyActor>();
        private RouteChoiceEncounterModifier _routeQuotaSource;
        private InputTelemetryOverlay _inputOverlay;
        private Rect[] _choiceObstacles = System.Array.Empty<Rect>();
        private GUIStyle _choiceHeadingStyle, _choiceBodyStyle, _choiceNoteStyle;

        public bool IsPaused => _paused;
        public bool IsGuideVisible => _showGuide;
        public bool IsCompletionPresented => _completionPresented;
        public bool ShouldShowWorldMarkers => !_paused && !_completionPresented;
        public int OffscreenThreatCount { get; private set; }
        public int ObservedLivingSummonCount { get; private set; }
        public int SummonExecutionReadyCount { get; private set; }
        public bool ShouldShowBossHud => _flow?.IsWardenEncounterActive == true &&
            _flow.Warden != null && _flow.Warden.IsAvailable;

        private void Start()
        {
            // Cache authored return actors before their quest-stage activation, once at startup.
            _combatTargets = Object.FindObjectsOfType<CombatTarget>(true);
            _summoners = Object.FindObjectsOfType<SummonerEnemyActor>(true);
            _authoredTargetCount = _combatTargets.Length;
            // Fixed per-owner slots join the existing marker/audio/threat loop. No global registry
            // or FindObjectsOfType during Update, and no allocation when a summon spawns/despawns.
            System.Array.Resize(ref _combatTargets, _authoredTargetCount + _summoners.Length * SummonerEnemyDefinition.MaximumLivingSummons);
            _choiceObstacles=new Rect[_combatTargets.Length+_summoners.Length+10];
            _inputOverlay=Object.FindObjectOfType<InputTelemetryOverlay>();
            int quotaSources=0;
            foreach (var modifier in Object.FindObjectsOfType<RouteChoiceEncounterModifier>())
                if (!modifier.IsReinforcementVariant) { _routeQuotaSource=modifier;quotaSources++; }
            if (quotaSources != 1) _routeQuotaSource=null; // Unknown authoring is not a guessed quota.
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
            _showGuide = false;
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

            if (_player?.Model != null)
            {
                _resources = HudResourcePresentation.Resolve(_player.Model.Health.Normalized,
                    _player.Model.Stamina.Normalized, _player.Model.State, _player.Model.HealingFlasks.CurrentCharges);
                _flaskStatus = _resources.FlaskLabel;
                int charges = _player.Model.HealingFlasks.CurrentCharges;
                int maximum = _player.Model.HealingFlasks.MaximumCharges;
                if (charges != _lastFlaskCharges || maximum != _lastFlaskMaximum)
                {
                    _lastFlaskCharges = charges; _lastFlaskMaximum = maximum;
                    _flaskCount = $"{charges} / {maximum}";
                }
            }
            RefreshSummonedTargets();
            if (!ShouldShowWorldMarkers)
            {
                OffscreenThreatCount = 0;
                return;
            }
            RefreshOffscreenThreatCount();
            for (int i = 0; i < _combatTargets.Length; i++)
            {
                CombatTarget target = _combatTargets[i];
                bool ready = target != null && target.isActiveAndEnabled && target.IsAvailable && target is IExecutionTarget execution &&
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

        private void RefreshSummonedTargets()
        {
            ObservedLivingSummonCount = 0; SummonExecutionReadyCount = 0;
            for (int owner = 0; owner < _summoners.Length; owner++)
                for (int slot = 0; slot < SummonerEnemyDefinition.MaximumLivingSummons; slot++)
                {
                    int index = _authoredTargetCount + owner * SummonerEnemyDefinition.MaximumLivingSummons + slot;
                    MeleeEnemyActor entity = _summoners[owner] == null ? null : _summoners[owner].GetLivingEntity(slot);
                    if (_combatTargets[index] != entity)
                    {
                        _combatTargets[index] = entity;
                        _executionWasReady[index] = false; // New identity in a reused slot is a new readiness edge.
                    }
                    if (entity == null) continue;
                    ObservedLivingSummonCount++;
                    if (ShouldShowWorldMarkers && entity.IsExecutionEligible && _player != null &&
                        (entity.transform.position - _player.transform.position).sqrMagnitude <= 144f) SummonExecutionReadyCount++;
                }
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

            using (EmberfallGuiTheme.Canvas())
            {
                DrawPlayerPanel();
                DrawQuestPanel();
                DrawBossHud();
                if (ShouldShowWorldMarkers)
                {
                    DrawInteractionPrompt();
                    DrawActionFeedback();
                    if (_showGuide) DrawGuide();
                    else GUI.Label(BottomLayout.Footer, "F1 操作指南 · F2 录制输入 · Esc 暂停", _bodyStyle);
                }
                if (_paused) DrawPausePanel();
                else if (_completionPresented) DrawCompletionPanel();
            }
            // Projection coordinates come from Camera pixels, not the logical UI canvas.
            if (ShouldShowWorldMarkers)
            {
                DrawTargetStatus();
                DrawSummonerStatus();
                DrawOffscreenThreats();
                DrawExecutionMarkers();
            }
        }

        private static PresentationBottomHudLayout BottomLayout =>
            PresentationBottomHudLayout.Resolve(EmberfallGuiTheme.Width, EmberfallGuiTheme.Height);

        private void DrawResourceLabel(Rect rect, string label, Color color, bool layered = false)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = color;
            if (layered) EmberfallGuiTheme.HudLabel(rect, label, _bodyStyle);
            else GUI.Label(rect, label, _bodyStyle);
            GUI.contentColor = previous;
        }

        private void DrawPlayerPanel()
        {
            Rect panel = ResolveTopLayout(!string.IsNullOrEmpty(_flow.SealConditionChecklist)).Player;
            EmberfallGuiTheme.HudFrame(panel,EmberfallGuiTheme.Ember);
            float x=panel.x+16, y=panel.y;
            EmberfallGuiTheme.HudLabel(new Rect(x,y+10,268,24),_flow.RouteTitle,_titleStyle);
            DrawResourceLabel(new Rect(x,y+39,120,20),_resources.HealthLabel,_resources.HealthAccent,true);
            EmberfallGuiTheme.HudLabel(new Rect(x+164,y+39,104,20),$"{_player.Model.Health.Current:0} / {_player.Model.Health.Maximum:0}",_bodyStyle);
            DrawBar(new Rect(x,y+64,268,12),_player.Model.Health.Normalized,EmberfallGuiTheme.Danger);
            DrawResourceLabel(new Rect(x,y+83,80,20),_resources.StaminaLabel,_resources.StaminaAccent,true);
            DrawBar(new Rect(x+88,y+88,180,7),_player.Model.Stamina.Normalized,EmberfallGuiTheme.Stamina);
            EmberfallGuiTheme.HudLabel(new Rect(x,y+107,64,20),"架势",_bodyStyle);
            DrawBar(new Rect(x+60,y+112,208,5),_player.Model.Posture.Normalized,EmberfallGuiTheme.Execution);
            string runeId = _player.ActiveRuneBlessing == RuneBlessing.Ember ? "text:ui.rune-status.ember" :
                _player.ActiveRuneBlessing == RuneBlessing.Guard ? "text:ui.rune-status.guard" : "text:ui.rune-status.none";
            EmberfallGuiTheme.HudLabel(new Rect(x,y+140,268,20),$"符文 · {Text(runeId)}",_bodyStyle);
            if (_player.HeavyPostureMultiplier > 1f)
                EmberfallGuiTheme.HudLabel(new Rect(x,y+166,268,20),$"武器加固 · 重击架势 +{_flow.SupplyCartRewardPercent}%",_bodyStyle);

            PresentationBottomHudLayout bottom = BottomLayout;
            DrawAbilitySlot(bottom.Flask,"R",_flaskStatus,
                _player.Model.State == CombatState.Heal ? _player.Model.StateNormalized : 0,_resources.FlaskAccent,_flaskCount);
            DrawAbilitySlot(bottom.Knife,"F",_player.Model.RangedCooldownRemaining<=0 ? "飞刀 · 就绪" : $"飞刀 {_player.Model.RangedCooldownRemaining:0.0}s",
                _player.Model.RangedCooldownNormalized);
            DrawAbilitySlot(bottom.Sweep,"V",_player.Model.SweepCooldownRemaining<=0 ? "横扫 · 就绪" : $"横扫 {_player.Model.SweepCooldownRemaining:0.0}s",
                _player.Model.SweepCooldownNormalized);
            if (_player.Model.CanUseGuardCounter || _player.Model.CurrentAttackIsGuardCounter || _player.IsGuardCounterReady)
            {
                Rect ready = new Rect(24,panel.yMax+12,300,38);
                EmberfallGuiTheme.Panel(ready,.94f,EmberfallGuiTheme.Execution);
                GUI.Label(new Rect(ready.x+12,ready.y,276,38),_player.Model.CanUseGuardCounter ? "◆ 反击就绪 · 左键" : _player.Model.CurrentAttackIsGuardCounter ? "◆ 反击中" : "◆ 守印蓄势",_centerStyle);
            }
        }

        private void DrawAbilitySlot(Rect rect,string key,string status,float busy,Color? accent=null,string quantity=null)
        {
            Color stateColor = accent ?? (busy>0 ? EmberfallGuiTheme.Line : EmberfallGuiTheme.Interaction);
            EmberfallGuiTheme.Panel(rect,.92f,stateColor);
            GUI.Label(new Rect(rect.x+12,rect.y+5,30,22),key,_titleStyle);
            if (quantity != null) GUI.Label(new Rect(rect.x+50,rect.y+5,60,22),quantity,_bodyStyle);
            DrawResourceLabel(new Rect(rect.x+12,rect.y+32,rect.width-20,24),status,accent ?? EmberfallGuiTheme.Text);
            if(busy>0) EmberfallGuiTheme.Bar(new Rect(rect.x+10,rect.yMax-4,rect.width-20,2),1-busy,EmberfallGuiTheme.Interaction);
        }

        private void DrawQuestPanel()
        {
            string checklist = _flow.SealConditionChecklist;
            bool showChecklist = !string.IsNullOrEmpty(checklist);
            // New choice/arrival receipts can wrap beyond one line; preserve the font and
            // existing regions, growing only the message allocation and its frame.
            var layout = ResolveTopLayout(showChecklist);
            Rect panel = layout.Quest;
            EmberfallGuiTheme.HudFrame(panel,EmberfallGuiTheme.Interaction);
            EmberfallGuiTheme.HudLabel(new Rect(panel.x + 16f, panel.y + 10f, panel.width - 32f, 26f), _flow.QuestTitle, _titleStyle);
            EmberfallGuiTheme.HudLabel(new Rect(panel.x + 16f, panel.y + 44f, panel.width - 32f, 46f), _flow.CurrentObjective, _objectiveStyle);
            if (showChecklist)
            {
                EmberfallGuiTheme.HudLabel(new Rect(panel.x + 16f, panel.y + 94f, panel.width - 32f, 54f), checklist, _bodyStyle);
            }
            EmberfallGuiTheme.HudLabel(layout.QuestMessage(showChecklist), _flow.LastMessage, _bodyStyle);
        }

        private PresentationHudLayout ResolveTopLayout(bool checklist)
        {
            float messageHeight = _bodyStyle.CalcHeight(new GUIContent(_flow.LastMessage), 298f);
            return PresentationHudLayout.Resolve(EmberfallGuiTheme.Width, checklist, messageHeight, _player.HeavyPostureMultiplier > 1f);
        }

        private void DrawBossHud()
        {
            if (!ShouldShowBossHud) return;
            var warden = _flow.Warden;
            Rect panel = ResolveTopLayout(!string.IsNullOrEmpty(_flow.SealConditionChecklist)).Boss;
            EmberfallGuiTheme.HudFrame(panel,EmberfallGuiTheme.Danger);
            EmberfallGuiTheme.HudLabel(new Rect(panel.x + 18f, panel.y + 7f, panel.width - 36f, 25f),
                $"{_flow.Resolve(warden.Definition.DisplayNameTextId)} · {ResolvePhaseName(warden.Phase)}", _titleStyle);
            DrawBar(new Rect(panel.x + 42f, panel.y + 39f, panel.width - 84f, 14f),
                warden.HealthNormalized, new Color(0.78f, 0.12f, 0.07f));
            DrawBar(new Rect(panel.x + 42f, panel.y + 59f, panel.width - 84f, 7f),
                warden.SecondaryResourceNormalized, new Color(0.12f, 0.62f, 0.9f));
            string hint = warden.State == WardenState.PhaseTransition ? "盾牌崩解 · 转换期间不可受击，观察场地符文" :
                warden.State == WardenState.Recovery ? WardenHudPresentation.AppendCommittedAttack(
                    "收招破绽 · 伤害提升", warden.State, warden.Brain.CurrentAttack) :
                warden.State == WardenState.GuardBreak ? "架势崩解 · 全力输出" :
                warden.State == WardenState.Windup ? $"预兆：{ResolveAttackName(warden.Brain.CurrentAttack)}" :
                warden.State == WardenState.Attack ? WardenHudPresentation.AppendCommittedAttack(
                    "攻击中", warden.State, warden.Brain.CurrentAttack) :
                warden.Phase == WardenPhase.PhaseOne ? "正面攻击削减盾架势，绕后或等待收招" :
                "盾已破碎 · 攻击同时削减生命与韧性";
            EmberfallGuiTheme.HudLabel(new Rect(panel.x + 18f, panel.y + 67f, panel.width - 36f, 36f), hint, _bodyStyle);
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
            _ => WardenHudPresentation.AttackName(attack)
        };

        private void DrawInteractionPrompt()
        {
            CombatTarget executionTarget = _player?.ExecutionPromptTarget;
            if (executionTarget != null)
            {
                Rect executePanel = BottomLayout.Interaction;
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
            if (TryReadChoicePreview(candidate,out var preview,out var choiceLayout)) DrawChoicePreview(preview,choiceLayout);
            Rect panel = BottomLayout.Interaction;
            EmberfallGuiTheme.Panel(panel,.94f,EmberfallGuiTheme.Interaction);
            GUI.Label(panel, $"E　{prompt}", _centerStyle);
        }

        public bool TryGetChoicePreview(out RouteChoicePreview preview, out RouteChoicePreviewLayout layout)
            => TryReadChoicePreview(_interactor?.CurrentCandidate,out preview,out layout);

        private bool TryReadChoicePreview(IInteractable candidate,out RouteChoicePreview preview,out RouteChoicePreviewLayout layout)
        {
            preview=default;layout=default;
            if (!ShouldShowWorldMarkers || _flow?.IsInitialized != true || _player?.Model == null ||
                _player.Model.IsDead || _player.Model.State == CombatState.Execution ||
                _player.ExecutionPromptTarget != null || _choiceObstacles.Length == 0) return false;
            if (!RouteChoicePreview.TryRead(candidate,_routeQuotaSource?.SupplyRouteMeleeQuota ?? 0,
                _routeQuotaSource?.RiskRouteMeleeQuota ?? 0,out preview)) return false;
            int count=0;float scale=EmberfallGuiTheme.Scale(Screen.width,Screen.height);
            var top=ResolveTopLayout(!string.IsNullOrEmpty(_flow.SealConditionChecklist));
            _choiceObstacles[count++]=top.Player;_choiceObstacles[count++]=top.Quest;
            float topBottom=Mathf.Max(top.Player.yMax,top.Quest.yMax);
            if (ShouldShowBossHud) { _choiceObstacles[count++]=top.Boss;topBottom=Mathf.Max(topBottom,top.Boss.yMax); }
            if (_showGuide) _choiceObstacles[count++]=BottomLayout.Guide;
            if (_inputOverlay != null && _inputOverlay.IsVisible) _choiceObstacles[count++]=Logical(_inputOverlay.PanelRect,scale);
            var camera=Camera.main;
            if (camera != null)
            {
                foreach (var owner in _summoners)
                    if (TryGetSummonerPanel(camera,owner,out Rect plate)) _choiceObstacles[count++]=Logical(plate,scale);
                foreach (var target in _combatTargets)
                {
                    if (target == null || !target.isActiveAndEnabled || !target.IsAvailable || !(target is IExecutionTarget execution) ||
                        (target.transform.position-_player.transform.position).sqrMagnitude > 144f) continue;
                    if (string.IsNullOrEmpty(ExecutionRules.MarkerTextId(execution.ExecutionKind,target.HealthNormalized,
                        execution.IsPostureExecutionWindow,execution.IsExecutionClaimed,target.SecondaryResourceNormalized))) continue;
                    Vector3 p=camera.WorldToScreenPoint(target.AimPoint.position+Vector3.up*.7f);if (p.z<=0) continue;
                    Rect marker=new Rect(p.x-104,Screen.height-p.y-22,208,28);
                    foreach (var owner in _summoners)
                        if (TryGetSummonerPanel(camera,owner,out Rect plate)) marker=AvoidSummonerPlate(marker,plate,Screen.height);
                    _choiceObstacles[count++]=Logical(marker,scale);
                }
                var locked=_targeting?.CurrentTarget;
                if (locked != null && locked.IsAvailable)
                {
                    Vector3 p=camera.WorldToScreenPoint(locked.AimPoint.position);
                    if (p.z>0) _choiceObstacles[count++]=Logical(locked is SummonerEnemyActor
                        ? new Rect(p.x-26,Screen.height-p.y-26,52,52)
                        : new Rect(p.x-230,Screen.height-p.y-95,460,125),scale);
                }
            }
            return RouteChoicePreviewLayout.TryResolve(EmberfallGuiTheme.Width,EmberfallGuiTheme.Height,topBottom,_choiceObstacles,count,out layout);
        }

        private static Rect Logical(Rect pixels,float scale) => new Rect(pixels.x/scale,pixels.y/scale,pixels.width/scale,pixels.height/scale);

        private void DrawChoicePreview(RouteChoicePreview preview,RouteChoicePreviewLayout layout)
        {
            _choiceHeadingStyle ??= EmberfallGuiTheme.Label(18,true);
            _choiceBodyStyle ??= EmberfallGuiTheme.Label(16);
            _choiceNoteStyle ??= EmberfallGuiTheme.Label(14);
            EmberfallGuiTheme.Panel(layout.Panel,.96f,EmberfallGuiTheme.Interaction);
            GUI.Label(layout.Title,preview.Title,_choiceHeadingStyle);
            DrawChoiceCard(layout.Left,preview.Left,preview.LeftBody,preview.SelectedIndex==0);
            DrawChoiceCard(layout.Right,preview.Right,preview.RightBody,preview.SelectedIndex==1);
            GUI.Label(layout.Note,RouteChoicePreview.Note,_choiceNoteStyle);
        }
        private void DrawChoiceCard(Rect card,string heading,string body,bool selected)
        {
            Color accent=selected?EmberfallGuiTheme.Interaction:EmberfallGuiTheme.Line;
            EmberfallGuiTheme.Panel(card,.84f,accent);
            if (selected) EmberfallGuiTheme.Fill(new Rect(card.x,card.y,card.width,2),accent);
            GUI.Label(RouteChoicePreviewLayout.Heading(card),heading,_choiceHeadingStyle);
            Color previous=GUI.contentColor;GUI.contentColor=selected?EmberfallGuiTheme.Interaction:EmberfallGuiTheme.Muted;
            GUI.Label(RouteChoicePreviewLayout.Badge(card),selected?"E · 当前目标":"对照选项",_choiceNoteStyle);GUI.contentColor=previous;
            GUI.Label(RouteChoicePreviewLayout.Body(card),body,_choiceBodyStyle);
        }

        private void DrawGuide()
        {
            Rect panel = BottomLayout.Guide;
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
            // The summoner owns one role-specific plate with health/posture/cast information.
            // Keep the lock brackets, not a second generic label underneath the same plate.
            if (target is SummonerEnemyActor) return;
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

        private void DrawSummonerStatus()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            for (int i = 0; i < _summoners.Length; i++)
            {
                SummonerEnemyActor actor = _summoners[i];
                if (!TryGetSummonerPanel(camera, actor, out Rect panel)) continue;
                var display = SummonerHudPresentation.Resolve(actor.Brain.State, actor.Brain.CurrentAttack,
                    actor.LivingEntityCount, actor.HasRecentSummonInterrupt, _targeting?.CurrentTarget == actor);
                EmberfallGuiTheme.Panel(panel, .9f, display.Accent);
                if (display.Emphasized) EmberfallGuiTheme.Fill(new Rect(panel.x, panel.y, panel.width, 2f), display.Accent);
                GUI.Label(new Rect(panel.x + 8f, panel.y + 3f, 304f, 22f), Text("text:enemy.ash-caller.name"), _centerStyle);
                if (display.TextId != null)
                {
                    Color previous = GUI.color; GUI.color = display.Emphasized ? display.Accent : EmberfallGuiTheme.Text;
                    GUI.Label(new Rect(panel.x + 8f, panel.y + 26f, 304f, 25f), Text(display.TextId), _centerStyle);
                    GUI.color = previous;
                }
                if (display.ShowCastProgress)
                    DrawBar(new Rect(panel.x + 16f, panel.y + 60f, 288f, 5f),
                        Mathf.Clamp01(actor.Brain.StateElapsed / actor.Brain.CurrentWindupDuration), new Color(.75f, .5f, 1f));
                else DrawBar(new Rect(panel.x + 16f, panel.y + 60f, 288f, 5f), actor.HealthNormalized, EmberfallGuiTheme.Danger);
                DrawBar(new Rect(panel.x + 16f, panel.y + 67f, 288f, 3f),
                    1f - actor.SecondaryResourceNormalized, ExecutionColor(actor));
            }
        }

        private bool TryGetSummonerPanel(Camera camera, SummonerEnemyActor actor, out Rect panel)
        {
            panel = default;
            if (actor == null || !actor.isActiveAndEnabled || !actor.HasSimulationAuthority || !actor.IsAvailable ||
                (actor.transform.position - _player.transform.position).sqrMagnitude > 256f) return false;
            // Quiet, unselected owners without summons need no empty title/health plate.
            // Keep the same test for execution-marker avoidance so invisible plates reserve no space.
            if (SummonerHudPresentation.Resolve(actor.Brain.State, actor.Brain.CurrentAttack,
                actor.LivingEntityCount, actor.HasRecentSummonInterrupt, _targeting?.CurrentTarget == actor).TextId == null) return false;
            Vector3 p = camera.WorldToScreenPoint(actor.AimPoint.position + Vector3.up * .4f);
            if (p.z <= 0f || p.x < 0f || p.x > Screen.width || p.y < 0f || p.y > Screen.height) return false;
            Vector3 top = camera.WorldToScreenPoint(actor.IdentityTopPoint);
            panel = ResolveSummonerPlate(new Vector2(top.x, Screen.height - top.y), Screen.width, Screen.height);
            return true;
        }

        public static Rect ResolveSummonerPlate(Vector2 identityTop, float screenWidth, float screenHeight)
        {
            float x = identityTop.x - 160f;
            float y = Mathf.Clamp(identityTop.y - 80f, 4f, Mathf.Max(4f, screenHeight - 76f));
            // At the upper edge, put the unchanged-size label beside the silhouette instead of over its crown.
            if (y + 72f > identityTop.y - 8f)
                x = identityTop.x + 48f + 320f <= screenWidth - 4f ? identityTop.x + 48f : identityTop.x - 368f;
            return new Rect(Mathf.Clamp(x, 4f, Mathf.Max(4f, screenWidth - 324f)), y, 320f, 72f);
        }

        public static Rect AvoidSummonerPlate(Rect marker, Rect plate, float screenHeight)
        {
            if (!marker.Overlaps(plate)) return marker;
            float below = plate.yMax + 6f;
            marker.y = below + marker.height <= screenHeight - 4f ? below : plate.yMin - marker.height - 6f;
            return marker;
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
                if (target == null || !target.isActiveAndEnabled || !target.IsAvailable || !(target is IExecutionTarget execution) ||
                    (target.transform.position - _player.transform.position).sqrMagnitude > 144f) continue;
                string marker = ExecutionRules.MarkerTextId(execution.ExecutionKind, target.HealthNormalized,
                    execution.IsPostureExecutionWindow, execution.IsExecutionClaimed,
                    target.SecondaryResourceNormalized);
                if (string.IsNullOrEmpty(marker)) continue;
                Vector3 screen = camera.WorldToScreenPoint(target.AimPoint.position + Vector3.up * 0.7f);
                if (screen.z <= 0f) continue;
                Rect markerRect = new Rect(screen.x - 104f, Screen.height - screen.y - 22f, 208f, 28f);
                foreach (var owner in _summoners)
                    if (TryGetSummonerPanel(camera, owner, out Rect plate)) markerRect = AvoidSummonerPlate(markerRect, plate, Screen.height);
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
                Rect panel = BottomLayout.Feedback;
                DrawPanel(panel, 0.92f);
                GUI.Label(panel, Text(feedback), _centerStyle);
            }
            if (Time.time < _executionTutorialUntil)
                GUI.Label(new Rect((EmberfallGuiTheme.Width - 660f) * 0.5f, EmberfallGuiTheme.Height - 205f, 660f, 35f),
                    Text("text:execution.tutorial"), _centerStyle);
        }

        private void DrawOffscreenThreats()
        {
            Camera gameplayCamera = Camera.main;
            if (gameplayCamera == null || _combatTargets == null) return;

            for (int i = 0; i < _combatTargets.Length; i++)
            {
                CombatTarget target = _combatTargets[i];
                if (target == null || !target.isActiveAndEnabled || target == _player || !target.IsAvailable || !target.IsThreatening) continue;
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
                if (target == null || !target.isActiveAndEnabled || target == _player || !target.IsAvailable || !target.IsThreatening) continue;
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
            EmberfallGuiTheme.Fill(new Rect(0,0,EmberfallGuiTheme.Width,EmberfallGuiTheme.Height),new Color(.01f,.035f,.04f,.7f));
            Rect panel = CenteredPanel(420f, 270f);
            DrawPanel(panel, 0.97f);
            GUILayout.BeginArea(new Rect(panel.x + 36f, panel.y + 28f, panel.width - 72f, panel.height - 56f));
            GUILayout.Label("游戏已暂停", _titleStyle);
            GUILayout.Space(24f);
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(.10f,.25f,.26f);
            if (GUILayout.Button("继续游戏", _buttonStyle, GUILayout.Height(44f)))
            {
                SetPaused(false);
            }

            GUILayout.Space(12f);
            if (GUILayout.Button("保存并返回主菜单", _buttonStyle, GUILayout.Height(44f)))
            {
                ReturnToMainMenu();
            }
            GUILayout.EndArea();
            GUI.backgroundColor = previousBackground;
        }

        private void DrawCompletionPanel()
        {
            EmberfallGuiTheme.Fill(new Rect(0,0,EmberfallGuiTheme.Width,EmberfallGuiTheme.Height),new Color(.01f,.035f,.04f,.8f));
            var layout = RouteCompletionLayout.Resolve(EmberfallGuiTheme.Width, EmberfallGuiTheme.Height);
            var facts = RouteCompletionPresentation.Read(_flow, _player);
            EmberfallGuiTheme.Panel(layout.Panel, .98f, EmberfallGuiTheme.Interaction);
            GUI.Label(layout.Title, "余烬封印已稳定", _completionTitleStyle);
            GUI.Label(layout.Subtitle, "主线已交付 · 存档旅程回顾", _completionNoteStyle);
            DrawCompletionCard(layout.Choices, "旅程抉择", facts.Choices, EmberfallGuiTheme.Interaction);
            DrawCompletionCard(layout.Rewards, "补给与探索", facts.Rewards, EmberfallGuiTheme.Execution);
            DrawCompletionCard(layout.Encounters, "可选遭遇状态", facts.Encounters, EmberfallGuiTheme.Line);
            DrawCompletionCard(layout.Journey, "存档累计记录", facts.Journey, EmberfallGuiTheme.Line);
            GUI.Label(layout.Scope, RouteCompletionPresentation.ScopeNote, _completionNoteStyle);
            if (EmberfallGuiTheme.Button(layout.Return, "保存并返回主菜单", _buttonStyle, true)) ReturnToMainMenu();
        }

        private void DrawCompletionCard(Rect card, string title, string body, Color accent)
        {
            EmberfallGuiTheme.Panel(card, .65f, accent);
            GUI.Label(RouteCompletionLayout.Heading(card), title, _completionHeadingStyle);
            GUI.Label(RouteCompletionLayout.Body(card), body, _completionBodyStyle);
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

        private static Rect CenteredPanel(float width, float height) =>
            EmberfallGuiTheme.Center(width,height);

        private void EnsureStyles()
        {
            _buttonStyle ??= EmberfallGuiTheme.ButtonStyle();
            _completionTitleStyle ??= EmberfallGuiTheme.Label(26, true, TextAnchor.MiddleCenter);
            _completionHeadingStyle ??= EmberfallGuiTheme.Label(17, true);
            _completionBodyStyle ??= EmberfallGuiTheme.Label(16, false, TextAnchor.UpperLeft);
            _completionNoteStyle ??= EmberfallGuiTheme.Label(14, false, TextAnchor.MiddleCenter);
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
                wordWrap = true,
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
            EmberfallGuiTheme.Panel(rect,alpha);
        }

        private static void DrawBar(Rect rect, float normalized, Color color)
        {
            EmberfallGuiTheme.Bar(rect,normalized,color);
        }
    }
}
