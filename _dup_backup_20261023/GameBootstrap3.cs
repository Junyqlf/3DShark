using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SandKill.Core;

namespace SandKill.Unity
{
    /// <summary>
    /// 3代「法术迸发」对局会话（2026-10-21）：组局、驱动回合循环。由 GameBootstrap.StartMode(2) 创建。
    /// 与2代（GameBootstrap2）并存：模式选择各自进入，互不影响。
    /// 牌库 = 2代47张 + 法术14张（炼金1/清晰2/点燃3/雷霆3/彗星3/屏障3）= 61张：每人10张+英雄牌（比1/2代多1张，稀释补偿），剩余作牌堆。
    /// UI 复用 GameUI2（自动识别 GameEngine3 启用法术相关界面）。
    /// </summary>
    public class GameBootstrap3 : MonoBehaviour, IGameSession
    {
        float IGameSession.AttackBackswing { get { return AttackBackswing; } }   // 字段留给 Inspector，属性走接口

        [Header("3代牌组（2代47张 + 法术14张 = 61张；2026-10-21 用户裁定：点燃/雷霆/彗星减至3张、新增屏障×3）")]
        public int WorkerCount = 3;
        public int UndeadCount = 4;
        public int ExorcistCount = 4;
        public int PriestCount = 4;
        public int PoliceCount = 4;
        public int GuardCount = 4;
        public int WitchCount = 3;
        public int ArsenalCount = 1;
        public int ArmedGuardCount = 1;
        public int MinisterCount = 4;
        public int GrandMinisterCount = 1;
        public int WardenCount = 3;
        public int MaidCount = 4;
        public int GreenAppleCount = 3;
        public int TerroristCount = 4;
        public int AlchemyCount = 1;    // 炼金术×1
        public int ClarityCount = 2;    // 清晰术×2
        public int IgniteCount = 3;     // 点燃×3（2026-10-21 由4减至3）
        public int ThunderCount = 3;    // 雷霆×3（2026-10-21 由4减至3）
        public int CometCount = 3;      // 彗星×3（2026-10-21 由4减至3）
        public int BarrierCount = 3;    // 屏障×3（2026-10-21 新增：1行动点获得1点持续2回合的护盾）

        [Header("节奏")]
        public float AIDelay = 0.9f;
        public float AttackBackswing = 1f;   // 攻击后摇：攻击结算后固定停顿，掩盖"对手有没有侍卫"的迟疑信号

        GameBootstrap _main;
        GameEngine3 _engine;
        GameUI2 _ui;
        int _gameIndex;
        int _baseSeed;

        public void Init(GameBootstrap main, int baseSeed)
        {
            _main = main;
            _baseSeed = baseSeed;
            NewGame();
        }

        void NewGame()
        {
            var deck = DeckSpec();
            // 对手数量来自主菜单（GameBootstrap.AICount）：2 个 AI=3 人局，3 个 AI=4 人局
            int total = Mathf.Clamp(_main.AICount, 1, 3) + 1;
            var names = new List<string> { "你" };
            var brains = new List<IPlayerBrain2> { new HumanBrain2() };
            for (int i = 1; i < total; i++)
            {
                names.Add("AI-" + i);
                int seed = _baseSeed + i * 101 + _gameIndex * 7;
                brains.Add(AIBrain2.CreateWithRandomStrategy(seed, i, total, seed ^ 0x5EED));
            }

            _engine = new GameEngine3(deck, names.ToArray(), brains,
                _baseSeed == 0 ? Environment.TickCount : _baseSeed + _gameIndex);
            foreach (var b in brains)
                if (b is AIBrain2 ai) { _engine.OnHostile += ai.NoteHostile; _engine.OnIntel += ai.NoteIntel; }

            var uiGO = new GameObject("GameUI3");
            _ui = uiGO.AddComponent<GameUI2>();
            _ui.Init(_engine, this);
            StartCoroutine(GameLoop());
        }

        /// <summary>当前牌库构成（帮助菜单"卡牌一览"页也读这份，保证和实际组牌一致）。</summary>
        public Dictionary<CardType, int> DeckSpec()
        {
            return new Dictionary<CardType, int>
            {
                { CardType.Worker, WorkerCount },
                { CardType.Undead, UndeadCount }, { CardType.Exorcist, ExorcistCount },
                { CardType.Priest, PriestCount }, { CardType.Police, PoliceCount },
                { CardType.Guard, GuardCount }, { CardType.Witch, WitchCount },
                { CardType.Arsenal, ArsenalCount }, { CardType.ArmedGuard, ArmedGuardCount },
                { CardType.Minister, MinisterCount }, { CardType.GrandMinister, GrandMinisterCount },
                { CardType.Warden, WardenCount }, { CardType.Maid, MaidCount },
                { CardType.GreenApple, GreenAppleCount }, { CardType.Terrorist, TerroristCount },
                { CardType.Alchemy, AlchemyCount }, { CardType.Clarity, ClarityCount },
                { CardType.Ignite, IgniteCount }, { CardType.Thunder, ThunderCount },
                { CardType.Comet, CometCount }, { CardType.Barrier, BarrierCount }
            };
        }

        public void Restart()
        {
            _gameIndex++;
            StopAllCoroutines();
            if (_ui != null) { Destroy(_ui.gameObject); _ui = null; }
            NewGame();
        }

        public void BackToMenu()
        {
            StopAllCoroutines();
            if (_ui != null) { Destroy(_ui.gameObject); _ui = null; }
            _main.ShowMenu();
            Destroy(gameObject);
        }

        IEnumerator GameLoop()
        {
            while (true)
            {
                if (_engine.GameOver) { _ui.Refresh(); yield return null; continue; }
                if (_engine.Pending != null) { _ui.Refresh(); yield return null; continue; }

                var cur = _engine.Players[_engine.Current];
                if (cur.IsHuman)
                {
                    // 真人回合：干等回合易主/Pending出现，绝不每帧 Refresh（点击稳定性，同2代）
                    int seat = _engine.Current;
                    while (!_engine.GameOver && _engine.Current == seat && _engine.Pending == null)
                        yield return null;
                    _ui.Refresh();
                }
                else
                {
                    yield return new WaitForSeconds(AIDelay);
                    if (_engine.Pending != null || _engine.GameOver) continue;
                    var legal = _engine.GetLegalActions(_engine.Current);
                    if (legal.Count == 0) { yield return null; continue; }
                    var brain = (AIBrain2)_engine.Brains[_engine.Current];
                    var act = brain.ChooseAction(_engine, _engine.Current, legal);
                    _engine.ApplyAction(act);
                    _ui.Refresh();
                    // 攻击后摇：固定停顿掩盖"对手是否在犹豫挡刀"的迟疑信号
                    if (act.Kind == ActionKind.Attack || act.Kind == ActionKind.ArsenalAttack || act.Kind == ActionKind.PoisonAttack)
                        yield return new WaitForSeconds(AttackBackswing);
                    else
                        yield return new WaitForSeconds(AIDelay * 0.5f);
                }
            }
        }
    }
}
