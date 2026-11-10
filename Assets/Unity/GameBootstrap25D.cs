using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SandKill.Core;

namespace SandKill.Unity
{
    /// <summary>
    /// 2.5D 棋盘对局会话（2026-11-09）：组局、驱动回合循环，表现层是 Board25D 而不是 GameUI2。
    /// 牌库与 3代（GameBootstrap3）完全一致——2代44张 + 法术17张 = 61张；
    /// 阶段1 先用 3代规则跑通棋盘，后续棋盘迭代再单独调牌组。
    /// </summary>
    public class GameBootstrap25D : MonoBehaviour, IGameSession
    {
        float IGameSession.AttackBackswing { get { return AttackBackswing; } }

        [Header("3代牌组（与 GameBootstrap3 相同；2026-10-23 用户裁定：青苹果/炼金术/清晰术退环境，屏障更名风墙，新增冰川×2/治疗术×3）")]
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
        public int GreenAppleCount = 0;
        public int TerroristCount = 4;
        public int IgniteCount = 3;
        public int ThunderCount = 3;
        public int CometCount = 3;
        public int BarrierCount = 3;
        public int GlacierCount = 2;
        public int HealCount = 3;

        [Header("节奏")]
        public float AIDelay = 0.9f;
        public float AttackBackswing = 1f;   // 攻击后摇：固定停顿，掩盖"对手有没有侍卫"的迟疑信号

        GameBootstrap _main;
        GameEngine3 _engine;
        Board25D _board;
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

            var boardGO = new GameObject("Board25D");
            _board = boardGO.AddComponent<Board25D>();
            _board.Init(_engine, this, 0);
            StartCoroutine(GameLoop());
        }

        /// <summary>当前牌库构成（与 GameBootstrap3 一致）。</summary>
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
                { CardType.Ignite, IgniteCount }, { CardType.Thunder, ThunderCount },
                { CardType.Comet, CometCount }, { CardType.Barrier, BarrierCount },
                { CardType.Glacier, GlacierCount }, { CardType.Heal, HealCount }
            };
        }

        public void Restart()
        {
            _gameIndex++;
            StopAllCoroutines();
            if (_board != null) { Destroy(_board.gameObject); _board = null; }
            NewGame();
        }

        public void BackToMenu()
        {
            StopAllCoroutines();
            if (_board != null) { Destroy(_board.gameObject); _board = null; }
            _main.ShowMenu();
            Destroy(gameObject);
        }

        IEnumerator GameLoop()
        {
            while (true)
            {
                if (_engine.GameOver) { _board.Refresh(); yield return null; continue; }
                if (_engine.Pending != null) { _board.Refresh(); yield return null; continue; }

                var cur = _engine.Players[_engine.Current];
                if (cur.IsHuman)
                {
                    // 真人回合：干等回合易主/Pending出现，绝不每帧 Refresh（点击稳定性，同2代）
                    int seat = _engine.Current;
                    while (!_engine.GameOver && _engine.Current == seat && _engine.Pending == null)
                        yield return null;
                    _board.Refresh();
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
                    _board.Refresh();
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
