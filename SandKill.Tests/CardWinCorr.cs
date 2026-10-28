using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

/// <summary>
/// 卡牌与胜利的相关性测试（新版引擎，默认AI）：600 局三人局。
/// 对 军机大臣 / 军火库 / 带刀侍卫 / 女巫 四张关键牌回答三个问题：
///   1. 终局持有者的胜率（含局数）——牌留得住吗？留下的人赢吗？
///   2. 技能使用者的胜率（含局数）——用出来了吗？用的人赢吗？
///   3. 赢家携带率——赢的局里有多少次赢家手里有这张牌？
/// 注意：相关性≠因果。AI 会按价值藏牌/留牌，赢家天然更可能握着好牌（选择偏差），
/// 本测试回答"这张牌与胜利同行了多远"，不回答"它带来了多少胜率"。
/// </summary>
public static class Program
{
    class Sample
    {
        public int UseN, UseWin;        // 技能使用者（胜率样本）
        public int WinnerHold;          // 终局在赢家手牌（局数）
        public int InDiscard;           // 终局躺在弃牌堆（局数）
    }

    public static int Main(string[] args)
    {
        int games = 600;
        if (args.Length > 0) int.TryParse(args[0], out games);
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var watch = new Dictionary<CardType, Sample>
        {
            { CardType.GrandMinister, new Sample() },
            { CardType.Arsenal, new Sample() },
            { CardType.ArmedGuard, new Sample() },
            { CardType.Witch, new Sample() },
        };

        int witchDoubleWinner = 0, finished = 0;

        for (int seed = 1; seed <= games; seed++)
        {
            var brains = new List<IPlayerBrain2>
            {
                new AIBrain2(seed * 3 + 1, 0, 3), new AIBrain2(seed * 3 + 2, 1, 3), new AIBrain2(seed * 3 + 3, 2, 3)
            };
            var e = new GameEngine2(Deck(), new[] { "P0", "P1", "P2" }, brains, seed);
            var usedExo = new bool[3];   // 勒索
            var usedArs = new bool[3];   // 军火库追攻
            var usedPoi = new bool[3];   // 毒药
            foreach (var b in brains)
            {
                var ai = (AIBrain2)b;
                e.OnHostile += (att, vic, kind) => ai.NoteHostile(att, vic, kind);
                e.OnIntel += ai.NoteIntel;
            }
            e.OnHostile += (att, vic, kind) =>
            {
                if (kind == ActionKind.Extort) usedExo[att] = true;
                if (kind == ActionKind.ArsenalAttack) usedArs[att] = true;
                if (kind == ActionKind.PoisonAttack) usedPoi[att] = true;
            };

            int guard2 = 0;
            while (!e.GameOver && guard2++ < 20000)
            {
                int s = e.Current;
                e.ApplyAction(brains[s].ChooseAction(e, s, e.GetLegalActions(s)));
            }
            if (!e.GameOver) continue;
            finished++;
            int w = e.WinnerSeat;

            // 带刀侍卫/解药的使用从战报补（这两类不走 OnHostile 攻击事件）
            var usedArm = new bool[3];
            var usedAnt = new bool[3];
            foreach (var line in e.History)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (line.StartsWith("P" + i + " "))
                    {
                        if (line.Contains("发动【带刀侍卫】")) usedArm[i] = true;
                        if (line.Contains("使用女巫·解药")) usedAnt[i] = true;
                    }
                }
            }

            for (int i = 0; i < 3; i++)
            {
                bool won = i == w;
                if (usedExo[i]) { watch[CardType.GrandMinister].UseN++; if (won) watch[CardType.GrandMinister].UseWin++; }
                if (usedArs[i]) { watch[CardType.Arsenal].UseN++; if (won) watch[CardType.Arsenal].UseWin++; }
                if (usedPoi[i]) { watch[CardType.Witch].UseN++; if (won) watch[CardType.Witch].UseWin++; }
                if (usedArm[i]) { watch[CardType.ArmedGuard].UseN++; if (won) watch[CardType.ArmedGuard].UseWin++; }
                if (usedAnt[i]) { /* 解药并入女巫使用统计 */ watch[CardType.Witch].UseN++; if (won) watch[CardType.Witch].UseWin++; }
            }

            var wHand = e.Players[w].Hand;
            foreach (var kv in watch)
            {
                if (wHand.Any(c => c.Type == kv.Key)) kv.Value.WinnerHold++;
                if (e.Discard.Any(c => c.Type == kv.Key)) kv.Value.InDiscard++;
            }
            if (wHand.Any(c => c.Type == CardType.Witch && !c.PoisonUsed && !c.AntidoteUsed)) witchDoubleWinner++;
        }

        Console.WriteLine("对局 " + finished + " 局（3人局，全员默认AI）——基准胜率 33.3%\n");
        Console.WriteLine("牌          技能使用者胜率      赢家终局持有率      终局在弃牌堆");
        foreach (var kv in watch)
        {
            var s = kv.Value;
            string u = s.UseN == 0 ? "—" : (100.0 * s.UseWin / s.UseN).ToString("F1") + "%(" + s.UseN + "人次)";
            string wn = (100.0 * s.WinnerHold / finished).ToString("F1") + "%";
            string d = (100.0 * s.InDiscard / finished).ToString("F1") + "%";
            Console.WriteLine(kv.Key.Name().PadRight(11) + u.PadRight(20) + wn.PadRight(20) + d);
        }
        Console.WriteLine("\n补充：赢家手里双药完好女巫的局数占比 " + (100.0 * witchDoubleWinner / finished).ToString("F1") + "%");
        Console.WriteLine("读法：使用者胜率>33.3% = 用这张牌的人更常赢；弃牌堆率 = 它最后烂在坟里的比例（活下来 vs 被抽走/用掉）。");
        return 0;
    }

    static Dictionary<CardType, int> Deck()
    {
        return new Dictionary<CardType, int>
        {
            { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
            { CardType.Police, 4 }, { CardType.Guard, 4 }, { CardType.Witch, 3 },
            { CardType.Arsenal, 1 }, { CardType.ArmedGuard, 1 }, { CardType.Minister, 4 },
            { CardType.GrandMinister, 1 }, { CardType.Warden, 3 }, { CardType.Maid, 4 }
        };
    }
}
