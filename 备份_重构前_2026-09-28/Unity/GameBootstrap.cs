using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SandKill.Core;

namespace SandKill.Unity
{
    /// <summary>
    /// 3D杀 入口脚本。
    /// 用法：场景中新建一个空物体（命名 GameBootstrap），把本脚本挂上去，直接点运行。
    /// UI 全部由代码在运行时生成，无需在场景中摆放任何控件。
    /// 卡组数量、AI数量、AI行动间隔都可以在 Inspector 里直接调整。
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("卡组构成（默认36张）")]
        public int WorkerCount = 12;
        public int ArsenalCount = 1;
        public int WitchCount = 3;
        public int UndeadCount = 4;
        public int ExorcistCount = 4;
        public int PriestCount = 4;
        public int PoliceCount = 4;
        public int GuardCount = 4;

        [Header("对局设置")]
        [Range(1, 3)] public int AICount = 2;            // AI对手数量（推荐2，即3人局）
        [Range(0.2f, 3f)] public float AIDelay = 0.9f;   // AI每步行动间隔（秒）
        public int RandomSeed = 0;                       // 0 = 每局随机

        GameEngine _engine;
        GameUI _ui;
        MenuUI _menu;
        int _gameIndex;

        void Start()
        {
            ShowMenu();
        }

        /// <summary>显示主菜单（游戏启动入口与"返回主菜单"共用）。</summary>
        public void ShowMenu()
        {
            StopAllCoroutines();
            if (_ui != null) { Destroy(_ui.gameObject); _ui = null; }
            if (_menu != null) Destroy(_menu.gameObject);
            var menuGO = new GameObject("MenuUI");
            _menu = menuGO.AddComponent<MenuUI>();
            _menu.Init(this);
        }

        /// <summary>从主菜单进入对局。0=初代经典，1=二代天赋觉醒。</summary>
        public void StartMode(int mode)
        {
            if (_menu != null) { Destroy(_menu.gameObject); _menu = null; }
            if (mode == 1)
            {
                var go = new GameObject("Game2Session");
                go.AddComponent<GameBootstrap2>().Init(this, RandomSeed);
                return;
            }
            NewGame();
        }

        /// <summary>终局后返回主菜单。</summary>
        public void BackToMenu()
        {
            ShowMenu();
        }

        void NewGame()
        {
            var deck = new Dictionary<CardType, int>
            {
                { CardType.Worker,   WorkerCount },
                { CardType.Arsenal,  ArsenalCount },
                { CardType.Witch,    WitchCount },
                { CardType.Undead,   UndeadCount },
                { CardType.Exorcist, ExorcistCount },
                { CardType.Priest,   PriestCount },
                { CardType.Police,   PoliceCount },
                { CardType.Guard,    GuardCount },
            };

            int total = AICount + 1;
            var names = new string[total];
            var brains = new IPlayerBrain[total];
            names[0] = "你";
            brains[0] = new HumanBrain();

            int baseSeed = RandomSeed != 0 ? RandomSeed : Environment.TickCount;
            for (int i = 1; i < total; i++)
            {
                names[i] = "AI-" + i;
                brains[i] = new AIBrain(baseSeed + i * 101 + _gameIndex * 7, i, total);
            }
            _gameIndex++;

            _engine = new GameEngine(deck, names, brains, baseSeed + _gameIndex * 13);

            // 把AI订阅到引擎的敌对行动事件上（记仇、记忆军火库持有者的信息来源）
            foreach (var b in brains)
            {
                var ai = b as AIBrain;
                if (ai != null) _engine.OnHostile += ai.NoteHostile;
            }

            var uiGO = new GameObject("GameUI");
            _ui = uiGO.AddComponent<GameUI>();
            _ui.Init(_engine, 0, this);

            StartCoroutine(GameLoop());
        }

        /// <summary>再来一局（终局面板按钮调用）。</summary>
        public void Restart()
        {
            StopAllCoroutines();
            if (_ui != null) Destroy(_ui.gameObject);
            NewGame();
        }

        IEnumerator GameLoop()
        {
            while (!_engine.GameOver)
            {
                int seat = _engine.Current;

                if (_engine.Players[seat].IsHuman)
                {
                    // 真人回合：操作全部由UI按钮驱动，这里只等回合易主
                    while (!_engine.GameOver && _engine.Current == seat)
                        yield return null;
                }
                else
                {
                    yield return new WaitForSeconds(AIDelay);
                    while (!_engine.GameOver && _engine.Current == seat)
                    {
                        // AI攻击真人可能触发真人的侍卫/牧师抉择 → 等UI提交
                        // 等待期间持续刷新界面，保证抉择按钮始终呈现
                        if (_engine.Pending != null)
                        {
                            _ui.Refresh();
                            yield return null;
                            continue;
                        }
                        var legal = _engine.GetLegalActions(seat);
                        if (legal.Count == 0) break;
                        var act = _engine.Brains[seat].ChooseAction(_engine, seat, legal);
                        _engine.ApplyAction(act);
                        _ui.Refresh();
                        yield return new WaitForSeconds(AIDelay);
                    }
                }
                _ui.Refresh();
            }
            _ui.Refresh(); // 终局展示
        }
    }
}
