using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

/// <summary>
/// 1v1 残局胜负归因（600局标准，3×AIBrain2）：着重英雄血量。
/// 在"首淘汰出局、局面转入1v1"的瞬间快照双方 HP/MaxHP/手牌数，
/// 按血量差分桶统计最终胜率，并与手牌差、行动先后做对照。
/// 血量差 = 己方HP − 对手HP（进入1v1时）。
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        int games = 600;
        if (args.Length > 0) int.TryParse(args[0], out games);
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 血量差分桶: index 0..6 → 差值 +3及以上,+2,+1,0,-1,-2,-3及以下（从己方视角）
        var hpBuckets = Enumerable.Range(0, 7).Select(_ => new int[2]).ToArray();   // [人次, 胜场]
        // 手牌差分桶（血量差=0 的平血量局内再看手牌差）
        var handBuckets = Enumerable.Range(0, 7).Select(_ => new int[2]).ToArray();
        // 行动先后：进入1v1后下一个行动者（非淘汰者）的胜率
        int nextActs = 0, nextActsWin = 0;
        // 绝对血量：1v1入场时双方HP分布
        var hpDist = new Dictionary<string, int>();
        int entries = 0, finished = 0;

        for (int seed = 1; seed <= games; seed++)
        {
            var brains = new List<IPlayerBrain2>
            {
                new AIBrain2(seed * 3 + 1, 0, 3), new AIBrain2(seed * 3 + 2, 1, 3), new AIBrain2(seed * 3 + 3, 2, 3)
            };
            var e = new GameEngine2(Deck(), new[] { "P0", "P1", "P2" }, brains, seed);
            foreach (var b in brains)
            {
                var ai = (AIBrain2)b;
                e.OnHostile += (a2, v2, k2) => ai.NoteHostile(a2, v2, k2);
                e.OnIntel += ai.NoteIntel;
            }

            int[] entry = null;   // [seatA, hpA, maxA, handA, seatB, hpB, maxB, handB, deadSeat]
            e.OnLog += line =>
            {
                if (entry != null) return;
                for (int i = 0; i < 3; i++)
                    if (line.StartsWith("P" + i + " HP归零，出局！"))
                    {
                        var alive = e.Players.Where(p => p.Alive).ToList();
                        if (alive.Count != 2) return;
                        int nxt = (e.Current + 1) % 3;
                        while (!e.Players[nxt].Alive) nxt = (nxt + 1) % 3;   // 1v1中下一行动者（入场时刻计算，非终局）
                        entry = new[] { alive[0].Seat, alive[0].HP, alive[0].MaxHP, alive[0].Hand.Count,
                                        alive[1].Seat, alive[1].HP, alive[1].MaxHP, alive[1].Hand.Count, nxt };
                    }
            };

            int guard = 0;
            while (!e.GameOver && guard++ < 20000)
            {
                int s = e.Current;
                e.ApplyAction(brains[s].ChooseAction(e, s, e.GetLegalActions(s)));
            }
            if (!e.GameOver || entry == null) continue;
            finished++; entries++;
            int w = e.WinnerSeat;

            // 双方视角各记一次（对称采样，每桶人次=2×局数）
            for (int v = 0; v < 2; v++)
            {
                int myHp = v == 0 ? entry[1] : entry[5];
                int myHand = v == 0 ? entry[3] : entry[7];
                int opHp = v == 0 ? entry[5] : entry[1];
                int opHand = v == 0 ? entry[7] : entry[3];
                int mySeat = v == 0 ? entry[0] : entry[4];
                int dHp = myHp - opHp, dHand = myHand - opHand;
                Add(hpBuckets, Bucket(dHp), mySeat == w);
                if (dHp == 0) Add(handBuckets, Bucket(dHand), mySeat == w);
            }
            // 入场HP分布
            string key = entry[1] + "vs" + entry[5];
            if (!hpDist.ContainsKey(key)) hpDist[key] = 0;
            hpDist[key]++;
            // 行动先后：进入1v1后下一行动者（淘汰者回合结束后轮到自己）
            int next = entry[8];
            nextActs++;
            if (next == w) nextActsWin++;
        }

        Console.WriteLine("对局 " + finished + " 局，1v1 入场样本 " + entries + " 组（双方视角各记一次，基准胜率 50%）\n");
        Console.WriteLine("【核心】1v1 入场时英雄血量差 → 最终胜率：");
        string[] labels = { "HP领先≥3", "HP领先2", "HP领先1", "HP持平", "HP落后1", "HP落后2", "HP落后≥3" };
        for (int i = 0; i < 7; i++)
            Console.WriteLine("  " + labels[i] + ": " + W(hpBuckets[i]));
        Console.WriteLine("\n【对照】HP持平的局内，手牌数差 → 胜率：");
        for (int i = 0; i < 7; i++)
            Console.WriteLine("  " + labels[i].Replace("HP", "手牌") + ": " + W(handBuckets[i]));
        Console.WriteLine("\n【对照】进入1v1后先手（对方回合刚结束、自己先行动）的胜率: " + W(new[] { nextActs, nextActsWin }));
        Console.WriteLine("\n【分布】1v1 入场双方 HP 组合（top 12）：");
        foreach (var kv in hpDist.OrderByDescending(k => k.Value).Take(12))
            Console.WriteLine("  " + kv.Key + ": " + kv.Value + "局");
        return 0;
    }

    static int Bucket(int d) { return d >= 3 ? 0 : d == 2 ? 1 : d == 1 ? 2 : d == 0 ? 3 : d == -1 ? 4 : d == -2 ? 5 : 6; }
    static void Add(int[][] b, int i, bool win) { b[i][0]++; if (win) b[i][1]++; }
    static string W(int[] b) { return b[0] == 0 ? "—" : (100.0 * b[1] / b[0]).ToString("F1") + "%(" + b[0] + ")"; }

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
