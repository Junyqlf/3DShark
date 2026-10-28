using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

namespace SandKill.AIBench
{
    /// <summary>
    /// 新旧AI对打基准：新版AIBrain2轮流坐0/1/2号位，对手是两个旧版AIBrain2V1。
    /// 若新旧同等水平，新版期望胜率≈33.3%；明显超出即为有效提升。
    /// </summary>
    class Program
    {
        static readonly Dictionary<CardType, int> Deck = new Dictionary<CardType, int>
        {
            { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
            { CardType.Police, 4 }, { CardType.Guard, 4 }, { CardType.Witch, 3 },
            { CardType.Arsenal, 1 }, { CardType.ArmedGuard, 1 }, { CardType.Minister, 4 },
            { CardType.GrandMinister, 1 }, { CardType.Warden, 3 }, { CardType.Maid, 4 }
        };

        public static int Main(string[] args)
        {
            int games = 900;
            if (args.Length > 0) int.TryParse(args[0], out games);
            var w = new AIWeights();
            if (args.Length > 1)
            {
                // 调参实验：nokill=关斩杀线 aggressive=早用毒药
                if (args[1] == "nokill") w.KillLineBonus = 0;
                else if (args[1] == "aggressive") w.PoisonLateTurn = 3;
            }

            int newWins = 0, finished = 0, newSeatViolations = 0;
            bool selfTest = args.Length > 1 && args[1] == "selftest";

            for (int seed = 1; seed <= games; seed++)
            {
                int newSeat = (seed - 1) % 3;   // 轮流坐三个位置，消除座位偏差
                var brains = new List<IPlayerBrain2>();
                for (int s = 0; s < 3; s++)
                    brains.Add(s == newSeat
                        ? (selfTest ? (IPlayerBrain2)new AIBrain2V1(seed * 7 + s, s, 3)
                                    : (IPlayerBrain2)new AIBrain2(seed * 7 + s, s, 3, w))
                        : (IPlayerBrain2)new AIBrain2V1(seed * 7 + s, s, 3));
                var e = new GameEngine2(Deck, new[] { "A", "B", "C" }, brains, seed);
                foreach (var b in brains)
                {
                    if (b is AIBrain2 n) { e.OnHostile += n.NoteHostile; e.OnIntel += n.NoteIntel; }
                    if (b is AIBrain2V1 o) e.OnHostile += o.NoteHostile;
                }
                int guard = 0;
                while (!e.GameOver && guard++ < 20000)
                {
                    int seat = e.Current;
                    e.ApplyAction(brains[seat].ChooseAction(e, seat, e.GetLegalActions(seat)));
                }
                if (!e.GameOver) continue;
                finished++;
                if (e.WinnerSeat == newSeat) newWins++;
                if (e.Pending != null) newSeatViolations++;
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            double wr = newWins * 100.0 / Math.Max(1, finished);
            Console.WriteLine("对局 " + finished + " 局，新版AI胜 " + newWins + " 局");
            Console.WriteLine("配置 " + (args.Length > 1 ? args[1] : "default") + "：新版AI胜率 " + wr.ToString("F1") + "%（同水平期望≈33.3%）"
                + (newSeatViolations == 0 ? "" : "  ⚠ pending残留 " + newSeatViolations));
            return 0;
        }
    }
}
