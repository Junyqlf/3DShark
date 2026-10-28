using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SandKill.Core;

namespace SandKill.Unity
{
    /// <summary>
    /// 2代对局会话：组局、驱动回合循环。由 GameBootstrap.StartMode(1) 创建。
    /// 支持 3/4 人局：对手数量取主菜单的选择（2 个 AI = 3 人局，3 个 AI = 4 人局；2026-10-12 起）。
    /// 47张牌库（44张+工人×3）：每人9张+英雄牌，剩余作牌堆。
    /// </summary>
    public class GameBootstrap2 : MonoBehaviour, IGameSession
    {
        float IGameSession.AttackBackswing { get { return AttackBackswing; } }   // 字段留给 Inspector，属性走接口

        [Header("2代牌组（共47张：44张+工人×3行动点+1；2026-10-13 工人回归牌库）")]
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

        [Header("节奏")]
        public float AIDelay = 0.9f;
        public float AttackBackswing = 1f;   // 攻击后摇：攻击结算后固定停顿，掩盖"对手有没有侍卫"的迟疑信号

        GameBootstrap _main;
        GameEngine2 _engine;
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
            // 对手数量来自主菜单（GameBootstrap.AICount）：2 个 AI=3 人局，3 个 AI=4 人局（2026-10-12 起真正生效）
            int total = Mathf.Clamp(_main.AICount, 1, 3) + 1;
            var names = new List<string> { "你" };
            var brains = new List<IPlayerBrain2> { new HumanBrain2() };
            for (int i = 1; i < total; i++)
            {
                names.Add("AI-" + i);
                // 策略随机（2026-10-12）：每个 AI 每局独立抽选 雨露均沾/对赌施压。
                // 2026-10-13 用户裁定：开局不公布策略（保持不可读），只在终局复盘界面可查（GameUI2.OpenReview）。
                int seed = _baseSeed + i * 101 + _gameIndex * 7;
                brains.Add(AIBrain2.CreateWithRandomStrategy(seed, i, total, seed ^ 0x5EED));
            }

            _engine = new GameEngine2(deck, names.ToArray(), brains,
                _baseSeed == 0 ? Environment.TickCount : _baseSeed + _gameIndex);
            foreach (var b in brains)
                if (b is AIBrain2 ai) { _engine.OnHostile += ai.NoteHostile; _engine.OnIntel += ai.NoteIntel; }

            var uiGO = new GameObject("GameUI2");
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
                { CardType.GreenApple, GreenAppleCount }, { CardType.Terrorist, TerroristCount }
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
                    // 真人回合：干等回合易主/Pending出现，绝不每帧 Refresh——
                    // Refresh 会重建手牌与对手面板，而点击需要"按下和松开落在同一对象上"，
                    // 每帧重建会导致所有点击永远不成（2代历史上的"点击无反应"大坑，初代因此用同款等待结构）。
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
                    // 攻击后摇：固定停顿掩盖"对手是否在犹豫挡刀"的迟疑信号（AI其实瞬间决策，不让玩家读出来）
                    if (act.Kind == ActionKind.Attack || act.Kind == ActionKind.ArsenalAttack || act.Kind == ActionKind.PoisonAttack)
                        yield return new WaitForSeconds(AttackBackswing);
                    else
                        yield return new WaitForSeconds(AIDelay * 0.5f);
                }
            }
        }
    }
}
