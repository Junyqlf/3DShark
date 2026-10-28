using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

namespace SandKill.Ablate
{
    /// <summary>
    /// AI 模块消融实验（3人局）：
    ///   对照组：三方全部模块开启 → 各座位胜率（公平性/先手偏差）。
    ///   消融组：每模块轮流关闭，局号轮转"关闭方"座位，其余两方全开 → 关闭方胜率。
    ///   模块价值 = 对照胜率 - 关闭方胜率（正值 = 该模块提升胜率）。
    /// 同时统计军火库与胜率的关系（开局发到/对局用过/赢家持有）。
    ///
    /// 编译（工作区根目录）：
    ///   csc /nologo /out:Ablate2.exe tests/Ablate2.cs unity_project/Core/*.cs
    /// </summary>
    class Program
    {
        const int CONTROL_GAMES = 3000;
        const int GAMES_PER_MODULE = 2000;

        static readonly Dictionary<CardType, int> Deck = new Dictionary<CardType, int>
        {
            { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
            { CardType.Police, 4 }, { CardType.Guard, 4 }, { CardType.Witch, 3 },
            { CardType.Arsenal, 1 }, { CardType.ArmedGuard, 1 }, { CardType.Minister, 4 },
            { CardType.GrandMinister, 1 }, { CardType.Warden, 3 }, { CardType.Maid, 4 }
        };

        // 模块清单：字段名 + 显示名
        static readonly (string Field, string Name)[] Modules =
        {
            ("RotateFire", "轮流火力索敌"),
            ("ThreatAssess", "威胁评估索敌"),
            ("Grudge", "记仇"),
            ("ArsenalMemory", "军火库记忆"),
            ("Desperate", "绝望阎王"),
            ("SmartExtort", "勒索纪律"),
            ("GuardDiscipline", "挡刀纪律"),
            ("ResourceDiscipline", "资源纪律"),
        };

        class GameResult
        {
            public int Winner = -1;            // -1 = 达到步数上限未分胜负
            public int ArsenalDealtSeat = -1;  // 开局发到军火库的座位（牌库只有1张，可能留在牌堆）
            public readonly bool[] UsedArsenal = new bool[3];  // 该座位是否发动过军火库攻击
            public int Turns;
        }

        static AIModules OffOne(string field)
        {
            var m = new AIModules();
            typeof(AIModules).GetField(field).SetValue(m, false);
            return m;
        }

        static GameResult Play(int seed, AIModules[] mods)
        {
            var brains = new List<IPlayerBrain2>();
            for (int s = 0; s < 3; s++) brains.Add(new AIBrain2(seed * 31 + s * 7 + 1, s, 3, null, mods[s]));
            var e = new GameEngine2(Deck, new[] { "A", "B", "C" }, brains, seed);
            foreach (var b in brains) { var ai = b as AIBrain2; if (ai != null) { e.OnHostile += ai.NoteHostile; e.OnIntel += ai.NoteIntel; } }
            var r = new GameResult();
            e.OnHostile += (att, vic, kind) => { if (kind == ActionKind.ArsenalAttack) r.UsedArsenal[att] = true; };
            for (int s = 0; s < 3; s++) if (e.Players[s].Hand.Any(c => c.Type == CardType.Arsenal)) r.ArsenalDealtSeat = s;
            int cap = 20000;
            while (!e.GameOver && cap-- > 0)
            {
                int seat = e.Current;
                e.ApplyAction(((AIBrain2)e.Brains[seat]).ChooseAction(e, seat, e.GetLegalActions(seat)));
            }
            r.Winner = e.GameOver ? e.WinnerSeat : -1;
            r.Turns = e.TurnNumber;
            return r;
        }

        public static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var t0 = DateTime.Now;

            // ---------- 对照组 ----------
            int[] ctrlWins = new int[3];
            int ctrlDraws = 0, ctrlTurns = 0;
            var ctrlResults = new List<GameResult>();
            for (int i = 0; i < CONTROL_GAMES; i++)
            {
                var r = Play(100000 + i * 17, new[] { new AIModules(), new AIModules(), new AIModules() });
                ctrlResults.Add(r);
                if (r.Winner < 0) ctrlDraws++; else ctrlWins[r.Winner]++;
                ctrlTurns += r.Turns;
            }
            double ctrlAvg = (ctrlWins[0] + ctrlWins[1] + ctrlWins[2]) / 3.0 / CONTROL_GAMES;
            Console.WriteLine("== 对照组（三方全开）" + CONTROL_GAMES + " 局 ==");
            for (int s = 0; s < 3; s++)
                Console.WriteLine("  座位" + s + " 胜率 " + P(ctrlWins[s], CONTROL_GAMES) + "（" + ctrlWins[s] + "/" + CONTROL_GAMES + "）");
            Console.WriteLine("  平均 " + (ctrlAvg * 100).ToString("F1") + "%   先手(座位0)偏差 " + ((ctrlWins[0] / (double)CONTROL_GAMES - ctrlAvg) * 100).ToString("F1") + "pt"
                + (ctrlDraws > 0 ? "   未分胜负 " + ctrlDraws + " 局" : ""));
            Console.WriteLine("  平均对局长度 " + (ctrlTurns / (double)CONTROL_GAMES).ToString("F1") + " 轮");

            // ---------- 消融组 ----------
            Console.WriteLine("\n== 消融组（关闭方 vs 两个全开方，" + GAMES_PER_MODULE + " 局/模块）==");
            Console.WriteLine("  模块           关闭方胜率        相对对照");
            foreach (var (field, name) in Modules)
            {
                int wins = 0, draws = 0, games = 0;
                for (int i = 0; i < GAMES_PER_MODULE; i++)
                {
                    int offSeat = i % 3;
                    var mods = new[] { new AIModules(), new AIModules(), new AIModules() };
                    mods[offSeat] = OffOne(field);
                    var r = Play(500000 + i * 17 + field.Length, mods);
                    games++;
                    if (r.Winner < 0) draws++;
                    else if (r.Winner == offSeat) wins++;
                }
                double rate = wins / (double)games;
                double se = Math.Sqrt(rate * (1 - rate) / games);
                double delta = (ctrlAvg - rate) * 100;
                Console.WriteLine("  " + name.PadRight(8) + "  " + (rate * 100).ToString("F1") + "%±" + (se * 100).ToString("F1") + "%   "
                    + (delta >= 0 ? "-" : "+") + Math.Abs(delta).ToString("F1") + "pt"
                    + (draws > 0 ? "  未分胜负" + draws : ""));
            }

            // ---------- 军火库统计（对照组样本，座位维度）----------
            Console.WriteLine("\n== 军火库与胜率（对照组，座位维度）==");
            int dealtGames = 0, dealtWins = 0;          // 开局发到军火库的座位
            int usedSeats = 0, usedSeatWins = 0;        // 对局中用过军火库攻击的座位
            int unusedSeats = 0, unusedSeatWins = 0;    // 没用过（且该局有人用过，作基准）
            int winnerDealt = 0;                        // 赢家=开局军火库持有者
            int winnerUsed = 0, winnerUnused = 0;       // 赢家是否用过军火库
            foreach (var r in ctrlResults)
            {
                if (r.Winner < 0) continue;
                if (r.ArsenalDealtSeat >= 0)
                {
                    dealtGames++;
                    if (r.Winner == r.ArsenalDealtSeat) { dealtWins++; winnerDealt++; }
                }
                bool anyUsed = r.UsedArsenal.Any(u => u);
                for (int s = 0; s < 3; s++)
                {
                    if (r.UsedArsenal[s]) { usedSeats++; if (r.Winner == s) usedSeatWins++; }
                    else if (anyUsed) { unusedSeats++; if (r.Winner == s) unusedSeatWins++; }
                }
                if (r.UsedArsenal[r.Winner]) winnerUsed++; else winnerUnused++;
            }
            Console.WriteLine("  开局发到军火库的座位：胜率 " + P(dealtWins, dealtGames) + "（" + dealtWins + "/" + dealtGames + "，基准 33.3%）");
            Console.WriteLine("  用过军火库攻击的座位：胜率 " + P(usedSeatWins, usedSeats) + "（" + usedSeatWins + "/" + usedSeats + "）");
            Console.WriteLine("  同局没用过军火库的座位：胜率 " + P(unusedSeatWins, unusedSeats) + "（" + unusedSeatWins + "/" + unusedSeats + "，基准 33.3%）");
            Console.WriteLine("  赢家是开局军火库持有者：占比 " + P(winnerDealt, dealtGames) + "（若无关应≈1/3）");
            Console.WriteLine("  赢家用过/没用过军火库攻击：" + winnerUsed + "/" + winnerUnused + " 局");

            Console.WriteLine("\n耗时 " + (DateTime.Now - t0).TotalSeconds.ToString("F0") + " 秒");
            return 0;
        }

        static string P(int x, int n) { return n == 0 ? "—" : (x * 100.0 / n).ToString("F1") + "%"; }
    }
}
