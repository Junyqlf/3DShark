using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

/// <summary>
/// 绝望报复模式 + 自认最强斩杀 专项验证（600局标准，3×AIBrain2）。
/// 问题1：绝望模式（手牌≤2 或 HP≤1 时威胁公式切换为结仇清算）是否真的触发、
///         触发后是否真的打"伤害我最深"（记仇值最高）的人？
/// 问题2：自认最强（手牌数全场第一）执行斩杀（淘汰对手）之后，是否真的更常获胜？
/// 记仇复刻 NoteHostile 语义：victim==我时 grudge[attacker]+=1（毒药+2），全部敌意种类计入。
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        int games = 600;
        if (args.Length > 0) int.TryParse(args[0], out games);
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // ---- 问题1：绝望模式 ----
        long despAtk = 0, despHitTop = 0, despWithGrudge = 0;   // 绝望攻击事件 / 命中记仇最高 / 有仇可报
        long normAtk = 0, normHitTop = 0, normWithGrudge = 0;
        long despHp1Only = 0, despHand2Only = 0, despBoth = 0;
        // ---- 问题2：斩杀与获胜 ----
        long killStrong = 0, killStrongWin = 0;       // 自认最强者完成的淘汰
        long killWeak = 0, killWeakWin = 0;           // 非最强者完成的淘汰
        long killProg = 0, killProgWin = 0;           // 最强者且目标生前HP≤1（斩杀程序命中）
        long kill3 = 0, kill3Win = 0, kill2 = 0, kill2Win = 0;  // 淘汰时存活人数
        long totalKills = 0;
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

            // 记仇矩阵（外部复刻）
            var grudge = new[] { new float[3], new float[3], new float[3] };
            // 每个受害者最近一次攻击类事件（用于斩杀归因：RaiseHostile先于扣血，快照=生前状态）
            int[] lastAtkKiller = { -1, -1, -1 };
            bool[] lastStrong = new bool[3];
            int[] lastVictimHp = new int[3];
            int[] lastAlive = new int[3];

            e.OnHostile += (a2, v2, k2) =>
            {
                if (a2 < 0 || a2 > 2 || v2 < 0 || v2 > 2 || a2 == v2) return;
                grudge[v2][a2] += k2 == ActionKind.PoisonAttack ? 2f : 1f;
                bool isAtk = k2 == ActionKind.Attack || k2 == ActionKind.ArsenalAttack || k2 == ActionKind.PoisonAttack;
                if (!isAtk) return;
                var me = e.Players[a2];
                if (!me.Alive) return;
                bool hp1 = me.HP <= 1, hand2 = me.Hand.Count <= 2;
                // 结仇最高者（并列都算命中），只统计"有仇可报"
                float g0 = a2 == 0 ? -1f : grudge[a2][0];
                float g1 = a2 == 1 ? -1f : grudge[a2][1];
                float g2 = a2 == 2 ? -1f : grudge[a2][2];
                float maxG = Math.Max(g0, Math.Max(g1, g2));
                bool hitTop = maxG > 0 && grudge[a2][v2] >= maxG;
                if (hp1 || hand2)
                {
                    despAtk++;
                    if (hitTop) despHitTop++;
                    if (maxG > 0) despWithGrudge++;
                    if (hp1 && hand2) despBoth++; else if (hp1) despHp1Only++; else despHand2Only++;
                }
                else
                {
                    normAtk++;
                    if (hitTop) normHitTop++;
                    if (maxG > 0) normWithGrudge++;
                }
                // 斩杀归因快照（生前）
                lastAtkKiller[v2] = a2;
                lastStrong[a2] = me.Hand.Count >= e.Players.Where(p => p.Alive && p.Seat != a2).Select(p => p.Hand.Count).DefaultIfEmpty(0).Max();
                lastVictimHp[v2] = e.Players[v2].HP;
                lastAlive[v2] = e.Players.Count(p => p.Alive);
            };

            var kills = new List<int[]>();   // [killer, strong, victimHpPre, aliveCount]
            e.OnLog += line =>
            {
                for (int i = 0; i < 3; i++)
                    if (line.StartsWith("P" + i + " HP归零，出局！"))
                        kills.Add(new[] { lastAtkKiller[i], lastAtkKiller[i] >= 0 ? (lastStrong[lastAtkKiller[i]] ? 1 : 0) : 0, lastVictimHp[i], lastAlive[i] });
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
            foreach (var k in kills)
            {
                if (k[0] < 0) continue;   // 无法归因（理论上不会发生）
                totalKills++;
                bool win = k[0] == w;
                if (k[1] == 1) { killStrong++; if (win) killStrongWin++; } else { killWeak++; if (win) killWeakWin++; }
                if (k[1] == 1 && k[2] <= 1) { killProg++; if (win) killProgWin++; }
                if (k[3] >= 3) { kill3++; if (win) kill3Win++; } else { kill2++; if (win) kill2Win++; }
            }
        }

        Console.WriteLine("对局 " + finished + " 局，基准胜率 33.3%（600局噪声±1.9%）\n");
        Console.WriteLine("【问题1 绝望模式】触发条件：手牌≤2 或 HP≤1，威胁公式切换为结仇清算（记仇×3）");
        Console.WriteLine("  攻击事件处于绝望状态: " + Pct(despAtk, despAtk + normAtk) + "  (HP≤1 only " + despHp1Only + " / 手牌≤2 only " + despHand2Only + " / 同时 " + despBoth + ")");
        Console.WriteLine("  绝望状态索敌命中率（有仇可报）: " + Pct(despHitTop, despWithGrudge) + "  (" + despHitTop + "/" + despWithGrudge + ")");
        Console.WriteLine("  平时状态索敌命中率（有仇可报）: " + Pct(normHitTop, normWithGrudge) + "  (" + normHitTop + "/" + normWithGrudge + ")");
        Console.WriteLine("\n【问题2 自认最强斩杀】IsStrongest=手牌数全场第一；斩杀=完成淘汰（生前HP见括号）");
        Console.WriteLine("  总淘汰数: " + totalKills);
        Console.WriteLine("  自认最强者淘汰对手: " + W(killStrongWin, killStrong));
        Console.WriteLine("  非最强者淘汰对手:   " + W(killWeakWin, killWeak));
        Console.WriteLine("  斩杀程序命中(最强且目标HP≤1): " + W(killProgWin, killProg));
        Console.WriteLine("  3人存活时完成淘汰:  " + W(kill3Win, kill3));
        Console.WriteLine("  1v1时完成淘汰:      " + W(kill2Win, kill2));
        return 0;
    }

    static string Pct(long a, long b) { return b == 0 ? "—" : (100.0 * a / b).ToString("F1") + "%"; }
    static string W(long win, long n) { return n == 0 ? "—" : (100.0 * win / n).ToString("F1") + "%(" + n + "次)"; }

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
