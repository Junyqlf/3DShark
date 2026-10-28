using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SandKill.Core;

namespace SandKill.Unity
{
    /// <summary>
    /// 2代对局会话：组局、驱动回合循环。由 GameBootstrap.StartMode(1) 创建。
    /// 2代固定3人局（30张牌只够3人：27张初始+3张牌堆）。
    /// </summary>
    public class GameBootstrap2 : MonoBehaviour
    {
        [Header("2代牌组（共37张，无工人）")]
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

        [Header("节奏")]
        public float AIDelay = 0.9f;

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
            var deck = new Dictionary<CardType, int>
            {
                { CardType.Undead, UndeadCount }, { CardType.Exorcist, ExorcistCount },
                { CardType.Priest, PriestCount }, { CardType.Police, PoliceCount },
                { CardType.Guard, GuardCount }, { CardType.Witch, WitchCount },
                { CardType.Arsenal, ArsenalCount }, { CardType.ArmedGuard, ArmedGuardCount },
                { CardType.Minister, MinisterCount }, { CardType.GrandMinister, GrandMinisterCount },
                { CardType.Warden, WardenCount }, { CardType.Maid, MaidCount }
            };
            var brains = new List<IPlayerBrain2> { new HumanBrain2() };
            for (int i = 1; i <= 2; i++)
                brains.Add(new AIBrain2(_baseSeed + i * 101 + _gameIndex * 7, i, 3));

            _engine = new GameEngine2(deck, new[] { "你", "AI-1", "AI-2" }, brains,
                _baseSeed == 0 ? Environment.TickCount : _baseSeed + _gameIndex);
            foreach (var b in brains)
                if (b is AIBrain2 ai) _engine.OnHostile += ai.NoteHostile;

            var uiGO = new GameObject("GameUI2");
            _ui = uiGO.AddComponent<GameUI2>();
            _ui.Init(_engine, this);
            StartCoroutine(GameLoop());
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
                    _ui.Refresh();
                    yield return null;
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
                    yield return new WaitForSeconds(AIDelay * 0.5f);
                }
            }
        }
    }
}
