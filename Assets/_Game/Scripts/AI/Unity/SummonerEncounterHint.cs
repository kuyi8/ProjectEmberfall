#if UNITY_EDITOR
using UnityEngine;
using Emberfall.AI.Domain;

namespace Emberfall.AI.Unity
{
    /// <summary>Editor-only trial guidance. Reads domain facts; never emits commands/damage/save facts.</summary>
    public sealed class SummonerEncounterHint : MonoBehaviour
    {
        [SerializeField] private SummonerEnemyActor _owner;
        private GUIStyle _label;
        private int _observedInterrupts;
        private float _receiptRemaining;
        public void Configure(SummonerEnemyActor owner) => _owner = owner;

        private void Update()
        {
            if (_owner == null || _owner.Brain == null) return;
            if (_owner.Brain.InterruptCount > _observedInterrupts) _receiptRemaining = 1.4f;
            if (_owner.Brain.InterruptCount < _observedInterrupts) _receiptRemaining = 0f;
            _observedInterrupts = _owner.Brain.InterruptCount;
            _receiptRemaining = Mathf.Max(0f, _receiptRemaining - Time.deltaTime);
        }

        private void OnGUI()
        {
            if (!UnityEditor.EditorApplication.isPlaying || _owner == null || _owner.Brain == null) return;
            if (_label == null) _label = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.UpperCenter, wordWrap = true };
            Rect panel = new Rect((Screen.width - 580f) * .5f, 16f, 580f, 146f);
            GUI.Box(panel, GUIContent.none);
            var brain = _owner.Brain;
            bool committed = brain.CurrentAttack == SummonerAttackKind.Summon &&
                (brain.State == RangedEnemyState.Release || brain.State == RangedEnemyState.Recovery);
            string state = !_owner.IsAvailable ? "唤骸者已倒下：灵骸随主人消散" :
                brain.IsSummoning ? "正在召唤！命中可打断读条" :
                committed ? "召唤已释放：此时命中不会撤回灵骸" :
                brain.CurrentAttack == SummonerAttackKind.Projectile &&
                (brain.State == RangedEnemyState.Windup || brain.State == RangedEnemyState.Release) ?
                (brain.LivingSummonCount == SummonerEnemyDefinition.MaximumLivingSummons ? "灵骸已满：改用弱弹施压" : "弱弹施压 · 可接近唤骸者") :
                "唤骸者试战 · 打断、清场或优先击杀主人";
            GUI.Label(new Rect(panel.x + 10f, panel.y + 8f, panel.width - 20f, 128f), state +
                "\n召唤物 " + _owner.LivingEntityCount + "/2    已打断 " + _owner.Brain.InterruptCount + " 次" +
                "\n紫色碎片冠＝临时灵骸；击杀主人可使其全部消散" +
                "\nF 飞刀打断   V 横扫清场   右键蓄力重击骷髅", _label);
            if (_receiptRemaining > 0f && _owner.IsAvailable)
                GUI.Label(new Rect(panel.x, panel.yMax + 4f, panel.width, 26f), "打断成功 · 本次没有生成灵骸", _label);
            if (_owner.Brain.IsSummoning)
            {
                float progress = Mathf.Clamp01(_owner.Brain.StateElapsed / _owner.Definition.SummonWindup);
                Rect bar = new Rect(panel.x + 16f, panel.yMax - 13f, panel.width - 32f, 5f);
                Color old = GUI.color;
                try { GUI.color = new Color(.9f, .48f, .14f); GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * progress, bar.height), Texture2D.whiteTexture); }
                finally { GUI.color = old; }
            }
        }
    }
}
#endif
