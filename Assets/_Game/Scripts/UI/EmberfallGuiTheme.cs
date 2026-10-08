using System;
using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.UI
{
    /// <summary>Shared transitional skin. Screen projection markers deliberately stay in pixel space.</summary>
    public static class EmberfallGuiTheme
    {
        public static readonly Color Ink = new Color(.025f, .075f, .085f);
        public static readonly Color Text = new Color(.92f, .94f, .87f);
        public static readonly Color Muted = new Color(.59f, .73f, .72f);
        public static readonly Color Line = new Color(.21f, .37f, .38f);
        public static readonly Color Interaction = new Color(.29f, .87f, .86f);
        public static readonly Color Execution = new Color(1f, .77f, .25f);
        public static readonly Color Danger = new Color(.94f, .31f, .22f);
        public static readonly Color Stamina = new Color(.32f, .8f, .64f);
        public static readonly Color Ember = new Color(.98f, .51f, .28f);

        public static float Scale(float width, float height) => Mathf.Min(width / 1280f, height / 800f);
        public static float Width => Screen.width / Scale(Screen.width, Screen.height);
        public static float Height => Screen.height / Scale(Screen.width, Screen.height);
        public static Rect Center(float width, float height) => new Rect((Width-width)*.5f, (Height-height)*.5f, width, height);
        public static CanvasScope Canvas() => new CanvasScope(Scale(Screen.width, Screen.height));

        public readonly struct CanvasScope : IDisposable
        {
            private readonly Matrix4x4 _previous;
            public CanvasScope(float scale)
            {
                _previous = GUI.matrix;
                GUI.matrix = _previous * Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            }
            public void Dispose() => GUI.matrix = _previous;
        }

        public static GUIStyle Label(int size, bool bold = false, TextAnchor alignment = TextAnchor.MiddleLeft)
            => new GUIStyle(GUI.skin.label)
            {
                font = RuntimeGuiFont.Chinese, fontSize = size,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = alignment, wordWrap = true,
                normal = { textColor = Text }, padding = new RectOffset(0,0,0,0)
            };

        public static GUIStyle ButtonStyle()
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                font = RuntimeGuiFont.Chinese, fontSize = 18, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft, padding = new RectOffset(18,18,0,0),
                border = new RectOffset(0,0,0,0)
            };
            style.normal.background = Texture2D.whiteTexture;
            style.hover.background = Texture2D.whiteTexture;
            style.active.background = Texture2D.whiteTexture;
            style.focused.background = Texture2D.whiteTexture;
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = Text;
            return style;
        }

        public static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color; GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
        }
        public static void Panel(Rect rect, float alpha = .94f, Color? accent = null)
        {
            Fill(new Rect(rect.x+3,rect.y+5,rect.width,rect.height), new Color(0,0,0,.24f));
            Fill(rect, new Color(Ink.r, Ink.g, Ink.b, alpha));
            Fill(new Rect(rect.x,rect.y,rect.width,1), Line);
            Fill(new Rect(rect.x,rect.y,3,rect.height), accent ?? Line);
        }
        // Persistent HUD only. Modal panels and physical-pixel world markers keep Panel.
        public static void HudFrame(Rect rect, Color accent)
        {
            Fill(rect, new Color(Ink.r, Ink.g, Ink.b, .16f));
            Fill(new Rect(rect.x, rect.y, rect.width, 1), new Color(Line.r, Line.g, Line.b, .65f));
            Fill(new Rect(rect.x, rect.y, 3, 30), accent);
            Fill(new Rect(rect.x, rect.yMax - 1, 24, 1), accent);
            Fill(new Rect(rect.xMax - 24, rect.yMax - 1, 24, 1), accent);
        }

        public static void HudLabel(Rect rect, string text, GUIStyle style)
        {
            if (!string.IsNullOrEmpty(text))
            {
                var content = new GUIContent(text);
                Vector2 size = style.CalcSize(content);
                size.y = style.CalcHeight(content, rect.width);
                Rect backing = HudTextBacking(rect, size, style.alignment);
                Fill(backing, new Color(Ink.r, Ink.g, Ink.b, .94f));
            }
            // Keep the original content rect/font/wrapping. Only its backing is fitted.
            GUI.Label(rect, text, style);
        }

        public static Rect HudTextBacking(Rect content, Vector2 measured, TextAnchor alignment)
        {
            float width = Mathf.Clamp(measured.x + 8f, 0f, content.width);
            float height = Mathf.Clamp(measured.y + 4f, 0f, content.height);
            int horizontal = (int)alignment % 3, vertical = (int)alignment / 3;
            float x = horizontal == 0 ? content.x : horizontal == 1
                ? content.center.x - width * .5f : content.xMax - width;
            float y = vertical == 0 ? content.y : vertical == 1
                ? content.center.y - height * .5f : content.yMax - height;
            return new Rect(x, y, width, height);
        }
        public static void Bar(Rect rect, float normalized, Color color)
        {
            Fill(rect, new Color(.08f,.15f,.16f,.96f));
            float progress = float.IsNaN(normalized) ? 0 : Mathf.Clamp01(normalized);
            Fill(new Rect(rect.x,rect.y,rect.width*progress,rect.height),color);
            Fill(new Rect(rect.x,rect.y,rect.width*progress,1), new Color(1,1,1,.3f));
        }
        public static bool Button(Rect rect, string label, GUIStyle style, bool primary = false)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = primary ? new Color(.16f,.4f,.39f) : new Color(.075f,.18f,.19f);
            bool clicked = GUI.Button(rect,label,style); GUI.backgroundColor = previous;
            Fill(new Rect(rect.x,rect.y,3,rect.height), primary ? Interaction : Line);
            return clicked;
        }
    }

    public readonly struct PresentationHudLayout
    {
        public PresentationHudLayout(Rect player, Rect quest, Rect boss)
        { Player=player; Quest=quest; Boss=boss; }
        public Rect Player { get; }
        public Rect Quest { get; }
        public Rect Boss { get; }
        public Rect QuestMessage(bool checklist) => new Rect(Quest.x + 16, Quest.y + (checklist ? 158 : 98), Quest.width - 32,
            Quest.height - (checklist ? 170 : 110));
        public static PresentationHudLayout Resolve(float width, bool checklist, float messageHeight = 0f, bool heavyReward = false)
        {
            var player = new Rect(24,24,300,heavyReward ? 196 : 170);
            float minimum = checklist ? 30 : 25;
            var quest = new Rect(width-354,24,330,Mathf.Max(checklist ? 190 : 140,
                (checklist ? 170 : 110) + Mathf.Max(minimum, messageHeight)));
            float free = quest.xMin-player.xMax-32;
            var boss = free >= 320
                ? new Rect(player.xMax+16,24,Mathf.Min(540,free),108)
                : new Rect((width-540)*.5f,Mathf.Max(player.yMax,quest.yMax)+16,540,108);
            return new PresentationHudLayout(player,quest,boss);
        }
    }

    /// <summary>Read-only display thresholds; never an input or combat eligibility authority.</summary>
    public readonly struct HudResourcePresentation
    {
        private readonly bool _terminal;
        private readonly bool _downed;
        private readonly bool _lowHealth;
        private readonly bool _lowStamina;
        private readonly FlaskDisplayState _flask;

        private enum FlaskDisplayState { Ready, Healing, Empty, FullHealth, Busy, Terminal }

        private HudResourcePresentation(bool terminal, bool downed, bool lowHealth, bool lowStamina, FlaskDisplayState flask)
        { _terminal = terminal; _downed = downed; _lowHealth = lowHealth; _lowStamina = lowStamina; _flask = flask; }

        public string HealthLabel => _terminal ? (_downed ? "已倒地" : "已阵亡") : _lowHealth ? "生命危急" : "生命";
        public string StaminaLabel => !_terminal && _lowStamina ? "耐力偏低" : "耐力";
        public string FlaskLabel => _flask switch
        {
            FlaskDisplayState.Healing => "治疗中",
            FlaskDisplayState.Empty => "药剂耗尽",
            FlaskDisplayState.FullHealth => "生命已满",
            FlaskDisplayState.Busy => "动作中",
            FlaskDisplayState.Terminal => "不可使用",
            _ => "药剂就绪"
        };
        public bool IsFlaskReady => _flask == FlaskDisplayState.Ready;
        public Color HealthAccent => _terminal || _lowHealth ? EmberfallGuiTheme.Danger : EmberfallGuiTheme.Text;
        public Color StaminaAccent => !_terminal && _lowStamina ? EmberfallGuiTheme.Execution : EmberfallGuiTheme.Text;
        public Color FlaskAccent => IsFlaskReady || _flask == FlaskDisplayState.Healing ? EmberfallGuiTheme.Interaction : EmberfallGuiTheme.Muted;

        public static HudResourcePresentation Resolve(float healthNormalized, float staminaNormalized,
            CombatState state, int flaskCharges, bool downed = false)
        {
            bool terminal = downed || state == CombatState.Dead;
            // These priorities mirror existing domain rejection reasons; there is no heal range/cooldown.
            FlaskDisplayState flask = terminal ? FlaskDisplayState.Terminal
                : state == CombatState.Heal ? FlaskDisplayState.Healing
                : flaskCharges <= 0 ? FlaskDisplayState.Empty
                : healthNormalized >= 1f ? FlaskDisplayState.FullHealth
                : state != CombatState.Locomotion ? FlaskDisplayState.Busy
                : FlaskDisplayState.Ready;
            return new HudResourcePresentation(terminal, downed, healthNormalized <= .25f,
                staminaNormalized <= .2f, flask);
        }
    }

    public readonly struct PresentationBottomHudLayout
    {
        private PresentationBottomHudLayout(float width, float height)
        {
            Flask = new Rect(24, height-116, 120, 64);
            Knife = new Rect(152, height-116, 120, 64);
            Sweep = new Rect(280, height-116, 120, 64);
            Interaction = new Rect((width-440)*.5f, height-100, 440, 48);
            Feedback = new Rect((width-540)*.5f, height-168, 540, 42);
            Guide = new Rect(24, height-358, 320, 226);
            Footer = new Rect(24, height-38, 300, 22);
        }
        public Rect Flask { get; }
        public Rect Knife { get; }
        public Rect Sweep { get; }
        public Rect Interaction { get; }
        public Rect Feedback { get; }
        public Rect Guide { get; }
        public Rect Footer { get; }
        public static PresentationBottomHudLayout Resolve(float logicalWidth, float logicalHeight)
            => new PresentationBottomHudLayout(logicalWidth, logicalHeight);
    }
}
