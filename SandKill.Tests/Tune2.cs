using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

namespace SandKill.Tune
{
    /// <summary>
    /// AI 权重调参对弈实验（3人局，锦标赛计分：第1名2分 / 第2名1分 / 第3名0分）。
    ///
    /// 设计：每局恰有一个"变体座位"使用待测权重，另两个座位用基线权重，局号轮转变体座位
    /// （消除先手偏差）。变体平均得分 &gt; 对手平均 = 调整变好。
    ///
    /// 第5轮：攻击分挂钩威胁评估。攻击分 = 轮转基础 + 威胁分 × ThreatInAttack。
    ///   基线 = 当前定稿（ThreatInAttack=0，完全解耦）+ 第3/4轮全部采用值
    ///   扫描：ThreatInAttack ∈ {0.25, 0.5, 0.75, 1.0}
    ///
    /// 编译（工作区根目录）：
    ///   csc /nologo /out:Tune2.exe tests/Tune2.cs unity_project/Core/*.cs
    /// </summary>
    class Program
    {
        const int GAMES_MAIN = 3000;
        const int GAMES_VARIANT = 2000;

        static readonly Dictionary<CardType, int> Deck = new Dictionary<CardType, int>
        {
            { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
            { CardType.Police, 4 }, { CardType.Guard, 4 }, { CardType.Witch, 3 },
            { CardType.Arsenal, 1 }, { CardType.ArmedGuard, 1 }, { CardType.Minister, 4 },
            { CardType.GrandMinister, 1 }, { CardType.Warden, 3 }, { CardType.Maid, 4 }
        };

        /// <summary>基线 = 上一轮的定稿，但新公式权重置0（旧行为近似）。</summary>
        static AIWeights OldW()
        {
            return new AIWeights { ExorciseUndeadBonus = 0f, BribeValueCoeff = 0f };
        }

        class GameResult
        {
            public readonly double[] Score = new double[3];   // 锦标赛分：2/1/0
            public int Winner = -1;
        }

        static GameResult Play(int seed, AIWeights[] w)
        {
            var brains = new List<IPlayerBrain2>();
            for (int s = 0; s < 3; s++) brains.Add(new AIBrain2(seed * 31 + s * 7 + 1, s, 3, w[s], null));
            var e = new GameEngine2(Deck, new[] { "A", "B", "C" }, brains, seed);
            foreach (var b in brains) { var ai = b as AIBrain2; if (ai != null) { e.OnHostile += ai.NoteHostile; e.OnIntel += ai.NoteIntel; } }
            int cap = 20000;
            while (!e.GameOver && cap-- > 0)
            {
                int seat = e.Current;
                e.ApplyAction(((AIBrain2)e.Brains[seat]).ChooseAction(e, seat, e.GetLegalActions(seat)));
            }
            var r = new GameResult();
            if (!e.GameOver) return r;
            r.Winner = e.WinnerSeat;
            r.Score[e.WinnerSeat] = 2;
            if (e.EliminationOrder.Count >= 2) r.Score[e.EliminationOrder[e.EliminationOrder.Count - 1]] = 1;  // 最后出局=第二名
            return r;
        }

        /// <summary>一对多：每局一个变体座位 vs 两个基线座位，轮转座位。</summary>
        static void RunSet(string title, Func<AIWeights> mkVariant, int games, int seedBase)
        {
            double varScore = 0, oppScore = 0;
            int varWins = 0, oppWins = 0, draws = 0;
            for (int i = 0; i < games; i++)
            {
                int vs = i % 3;
                var w = new[] { OldW(), OldW(), OldW() };
                w[vs] = mkVariant();
                var r = Play(seedBase + i * 17, w);
                if (r.Winner < 0) { draws++; continue; }
                varScore += r.Score[vs];
                varWins += r.Winner == vs ? 1 : 0;
                for (int s = 0; s < 3; s++) if (s != vs) { oppScore += r.Score[s]; oppWins += r.Winner == s ? 1 : 0; }
            }
            double n = games - draws;
            double vAvg = varScore / n, oAvg = oppScore / (2 * n);
            double vWin = varWins / n, oWin = oppWins / (2 * n);
            Console.WriteLine("  " + title.PadRight(24)
                + " 得分 " + vAvg.ToString("F3") + " vs 对手 " + oAvg.ToString("F3")
                + "  Δ" + ((vAvg - oAvg) >= 0 ? "+" : "") + (vAvg - oAvg).ToString("F3")
                + "   胜率 " + (vWin * 100).ToString("F1") + "% vs " + (oWin * 100).ToString("F1") + "%"
                + (draws > 0 ? "  未分胜负" + draws : ""));
        }

        public static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var t0 = DateTime.Now;

            Console.WriteLine("== 基线自检（三方全基线=当前定稿，座位0得分≈1.1——先手优势）==");
            {
                double sum = 0; int games = 1500, draws = 0;
                for (int i = 0; i < games; i++)
                {
                    var r = Play(700000 + i * 17, new[] { OldW(), OldW(), OldW() });
                    if (r.Winner < 0) { draws++; continue; }
                    sum += r.Score[0];
                }
                Console.WriteLine("  座位0 平均得分 " + (sum / (games - draws)).ToString("F3") + "（" + (games - draws) + "局）");
            }

            Console.WriteLine("\n== 第6轮：辅助技三公式（警察轮转+威胁/驱魔亡灵计数/大臣贿赂公式）==");
            RunSet("三公式默认(亡灵2/贿赂1)", NewWDefault, GAMES_MAIN, 7100000);
            RunSet("亡灵权重1", () => { var w = NewWDefault(); w.ExorciseUndeadBonus = 1f; return w; }, GAMES_VARIANT, 7400000);
            RunSet("亡灵权重3", () => { var w = NewWDefault(); w.ExorciseUndeadBonus = 3f; return w; }, GAMES_VARIANT, 7600000);
            RunSet("贿赂系数0.5", () => { var w = NewWDefault(); w.BribeValueCoeff = 0.5f; return w; }, GAMES_VARIANT, 7800000);
            RunSet("贿赂系数2", () => { var w = NewWDefault(); w.BribeValueCoeff = 2f; return w; }, GAMES_VARIANT, 8000000);

            Console.WriteLine("\n耗时 " + (DateTime.Now - t0).TotalSeconds.ToString("F0") + " 秒");
            return 0;
        }

        static AIWeights NewWDefault() { return new AIWeights(); }   // 当前全机制默认
    }
}
