using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

namespace SandKill.WinRate
{
    /// <summary>
    /// 6英雄胜率仿真：全AI 3人局，英雄由引擎每局随机分配3个（互不重复）。
    /// 统计每个英雄的：出场次数、胜场、胜率（胜场/出场）、出场率。
    /// </summary>
    class Program
    {
        public static int Main(string[] args)
        {
            int games = 600;
            if (args.Length > 0) int.TryParse(args[0], out games);
            int sacStyle = 2;
            if (args.Length > 1) int.TryParse(args[1], out sacStyle);   // 牺牲挡刀风格：1优先牧师 2优先侍卫 3 EV>10

            var deck = new Dictionary<CardType, int>
            {
                { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
                { CardType.Police, 4 }, { CardType.Guard, 4 }, { CardType.Witch, 3 },
                { CardType.Arsenal, 1 }, { CardType.ArmedGuard, 1 }, { CardType.Minister, 4 },
                { CardType.GrandMinister, 1 }, { CardType.Warden, 3 }, { CardType.Maid, 4 }
            };

            var heroNames = GameEngine2.HeroRoster.ToDictionary(h => h.Id, h => h.Name);
            var appeared = new int[GameEngine2.HeroRoster.Length];
            var wins = new int[GameEngine2.HeroRoster.Length];
            int finished = 0;
            long totalTurns = 0;

            for (int seed = 1; seed <= games; seed++)
            {
                var brains = new List<IPlayerBrain2>
                {
                    new AIBrain2(seed * 3 + 1, 0, 3), new AIBrain2(seed * 3 + 2, 1, 3), new AIBrain2(seed * 3 + 3, 2, 3)
                };
                var e = new GameEngine2(deck, new[] { "AI-0", "AI-1", "AI-2" }, brains, seed);
                e.PriestSacStyle = sacStyle;
                foreach (var b in brains) { var ai = b as AIBrain2; if (ai != null) { e.OnHostile += ai.NoteHostile; e.OnIntel += ai.NoteIntel; } }

                foreach (var p in e.Players) appeared[e.HeroOf(p.Seat).Id]++;

                int guard = 0;
                while (!e.GameOver && guard++ < 20000)
                {
                    int seat = e.Current;
                    e.ApplyAction(((AIBrain2)e.Brains[seat]).ChooseAction(e, seat, e.GetLegalActions(seat)));
                }
                if (!e.GameOver) continue;   // 超时未分出胜负的局不计入
                finished++;
                totalTurns += e.TurnNumber;
                wins[e.HeroOf(e.WinnerSeat).Id]++;
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("对局 " + finished + " 局（目标 " + games + "，牺牲风格 " + sacStyle + "），平均轮数 " + (finished > 0 ? totalTurns / (double)finished : 0).ToString("F1"));
            Console.WriteLine();
            Console.WriteLine("英雄".PadRight(16) + "出场".PadLeft(6) + "胜场".PadLeft(6) + "胜率".PadLeft(8) + "出场率".PadLeft(8));
            foreach (var h in GameEngine2.HeroRoster.OrderByDescending(h => wins[h.Id]))
            {
                double wr = appeared[h.Id] > 0 ? wins[h.Id] * 100.0 / appeared[h.Id] : 0;
                Console.WriteLine((h.Id + "." + h.Name).PadRight(16)
                    + appeared[h.Id].ToString().PadLeft(6)
                    + wins[h.Id].ToString().PadLeft(6)
                    + (wr.ToString("F1") + "%").PadLeft(8)
                    + (appeared[h.Id] * 100.0 / Math.Max(1, finished * 3)).ToString("F1").PadLeft(7) + "%");
            }
            return 0;
        }
    }
}
