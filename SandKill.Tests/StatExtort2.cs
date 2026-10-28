using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

/// <summary>
/// 军机大臣胜率归因深挖（600局标准）。四个独立检验：
///   A. 持有者对照：同样摸到军机大臣，"用了"vs"没用"的胜率（排除抽牌运气的选择偏差）
///   B. 树敌检验：勒索后6戳(≈2轮)内勒索者挨的打 vs 之前6戳
///   C. 节奏检验：勒索当回合勒索者的攻击次数 vs 其平时均值（机会成本）
///   D. 状态偏差：勒索瞬间勒索者的HP/手牌全场排名（是不是只有快死的人才勒索）
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        int games = 600;
        if (args.Length > 0) int.TryParse(args[0], out games);
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // A. 持有者对照
        var holdUsed = new int[2]; var holdNotUsed = new int[2]; var noHold = new int[2];
        int gmGames = 0;
        // B. 树敌
        int retalWindows = 0; double hitsBefore = 0, hitsAfter = 0, othersAfter = 0, othersBefore = 0;
        // C. 节奏
        int extortTurns = 0; double atkOnExtortTurn = 0; double atkOnNormalTurn = 0; int normalTurns = 0;
        // D. 状态偏差
        int stateSamples = 0; int hpRankSum = 0, handRankSum = 0; int hpLowest = 0, handLowest = 0;
        int finished = 0;

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

            // 开局谁摸到了军机大臣
            int gmHolder = -1;
            for (int i = 0; i < 3; i++)
                if (e.Players[i].Hand.Any(c => c.Type == CardType.GrandMinister)) { gmHolder = i; break; }
            if (gmHolder >= 0) gmGames++;

            // 攻击事件流（带回合戳）
            var attacks = new List<int[]>();   // [stamp, attacker, victim]
            e.OnHostile += (a2, v2, k2) =>
            {
                if (k2 == ActionKind.Attack || k2 == ActionKind.ArsenalAttack || k2 == ActionKind.PoisonAttack)
                    attacks.Add(new[] { e.CurrentStamp, a2, v2 });
            };
            // 勒索事件（带戳、带【当时】全场HP/手牌快照——必须在OnLog回调内捕获，事后读的是终局状态）
            var extorts = new List<int[]>();   // [stamp, seat, hp0,hp1,hp2, hand0,hand1,hand2]
            e.OnLog += line =>
            {
                for (int i = 0; i < 3; i++)
                    if (line.StartsWith("P" + i + " 发动【军机大臣·勒索】"))
                        extorts.Add(new[] { e.CurrentStamp, i,
                            e.Players[0].HP, e.Players[1].HP, e.Players[2].HP,
                            e.Players[0].Hand.Count, e.Players[1].Hand.Count, e.Players[2].Hand.Count });
            };

            int guard = 0;
            while (!e.GameOver && guard++ < 20000)
            {
                int s = e.Current;
                e.ApplyAction(brains[s].ChooseAction(e, s, e.GetLegalActions(s)));
            }
            if (!e.GameOver) continue;
            finished++;
            int w = e.WinnerSeat;

            // ---- A. 持有者对照 ----
            bool used = extorts.Any(x => x[1] == gmHolder);
            for (int i = 0; i < 3; i++)
            {
                var bucket = (i == gmHolder) ? (used ? holdUsed : holdNotUsed) : noHold;
                bucket[0]++;
                if (i == w) bucket[1]++;
            }

            // ---- B/C/D ----
            foreach (var ev2 in extorts)
            {
                int st = ev2[0], s = ev2[1];
                // B. 树敌：前6戳 vs 后6戳，打向s的次数；对照组=同时段打向其他人的平均
                double before = attacks.Count(a2 => a2[2] == s && a2[0] >= st - 6 && a2[0] < st);
                double after = attacks.Count(a2 => a2[2] == s && a2[0] > st && a2[0] <= st + 6);
                double othBefore = attacks.Count(a2 => a2[2] != s && a2[0] >= st - 6 && a2[0] < st) / 2.0;
                double othAfter = attacks.Count(a2 => a2[2] != s && a2[0] > st && a2[0] <= st + 6) / 2.0;
                retalWindows++; hitsBefore += before; hitsAfter += after;
                othersBefore += othBefore; othersAfter += othAfter;
                // C. 节奏：勒索当回合的攻击次数（同戳同攻）
                double atkThis = attacks.Count(a2 => a2[1] == s && a2[0] == st);
                atkOnExtortTurn += atkThis; extortTurns++;
                // D. 状态偏差：勒索【当场】HP/手牌排名（0=最低），用回调内捕获的快照
                int hpSelf = ev2[2 + s], handSelf = ev2[5 + s];
                int hpRank = 0, handRank = 0;
                for (int i = 0; i < 3; i++)
                {
                    if (i == s) continue;
                    if (ev2[2 + i] > hpSelf) hpRank++;
                    if (ev2[5 + i] > handSelf) handRank++;
                }
                stateSamples++; hpRankSum += hpRank; handRankSum += handRank;
                if (hpRank == 0) hpLowest++;
                if (handRank == 0) handLowest++;
            }
            // C. 平时均值：该玩家没有勒索的回合的攻击次数
            var turnStamps = attacks.Select(a2 => a2[0]).Distinct().ToList();
            foreach (var ts in turnStamps)
            {
                var anyExt = extorts.FirstOrDefault(x => x[0] == ts);
                if (anyExt != null) continue;   // 勒索回合已在上面统计
                // 找出这个戳的进攻方（同一戳只有一个当前玩家）
                int attacker = attacks.First(a2 => a2[0] == ts)[1];
                atkOnNormalTurn += attacks.Count(a2 => a2[0] == ts && a2[1] == attacker);
                normalTurns++;
            }
        }

        Console.WriteLine("对局 " + finished + " 局（" + gmGames + " 局有军机大臣出场），基准胜率 33.3%\n");
        Console.WriteLine("【A 持有者对照】排除抽牌运气后，'用勒索'到底亏不亏：");
        Console.WriteLine("  持有者用了勒索:  " + W(holdUsed));
        Console.WriteLine("  持有者没用勒索:  " + W(holdNotUsed));
        Console.WriteLine("  没摸到的人:      " + W(noHold));
        Console.WriteLine("\n【B 树敌检验】勒索者被打的次数（前后各≈2轮，对照组=同时段其他人平均）：");
        Console.WriteLine("  勒索者: 前 " + F(hitsBefore / retalWindows) + " → 后 " + F(hitsAfter / retalWindows));
        Console.WriteLine("  其他人: 前 " + F(othersBefore / retalWindows) + " → 后 " + F(othersAfter / retalWindows));
        Console.WriteLine("\n【C 节奏检验】勒索当回合 vs 平时回合的攻击次数（机会成本）：");
        Console.WriteLine("  勒索当回合: " + F(atkOnExtortTurn / extortTurns) + " 次   平时回合: " + F(atkOnNormalTurn / normalTurns) + " 次");
        Console.WriteLine("\n【D 状态偏差】勒索瞬间勒索者的全场排名（0=最差，2=最好）：");
        Console.WriteLine("  平均HP排名: " + F(1.0 * hpRankSum / stateSamples) + "（HP最低者占 " + (100.0 * hpLowest / stateSamples).ToString("F1") + "%）");
        Console.WriteLine("  平均手牌排名: " + F(1.0 * handRankSum / stateSamples) + "（手牌最少者占 " + (100.0 * handLowest / stateSamples).ToString("F1") + "%）");
        return 0;
    }

    static string W(int[] b) { return b[0] == 0 ? "—" : (100.0 * b[1] / b[0]).ToString("F1") + "%(" + b[0] + "人次)"; }
    static string F(double v) { return v.ToString("F2"); }

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
