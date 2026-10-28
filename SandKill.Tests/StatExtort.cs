using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

/// <summary>
/// 军机大臣勒索专项统计（新版引擎，默认AI，600局标准）：
///   AI 都勒索了什么牌型、各牌型收益如何（张数/空手率）、用勒索的人到底赢没赢。
/// 回答"军机大臣为什么是负相关"——是乱勒索，还是收益没兑现。
/// 全部从战报 History 事后解析（同步结算保证事件与结果行相邻）。
/// </summary>
public static class Program
{
    class Ev
    {
        public int Times;            // 勒索事件数
        public int Cards;            // 总张数收益
        public int Whiff;            // 事件总收益为0的次数
        public int UseN, UseWin;     // 事件使用者人次与其中胜场
    }

    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine("CRASH: " + ex.GetType().FullName + " | " + ex.Message);
            return 1;
        }
    }

    static int Run(string[] args)
    {
        int games = 600;
        if (args.Length > 0) int.TryParse(args[0], out games);
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var byType = new Dictionary<string, Ev>();
        foreach (var t in GameEngine2.ExtortTypes.Concat(new[] { CardType.Hero }))
            byType[CardDefs.Of(t).Name] = new Ev();
        int finished = 0, totalExtorts = 0, saveTurnExtorts = 0;
        var buckets = new Dictionary<string, int[]> { { "用过但全空", new int[2] }, { "1-2张", new int[2] }, { "3张以上", new int[2] } };

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
                e.OnHostile += (a, v, k) => ai.NoteHostile(a, v, k);
                e.OnIntel += ai.NoteIntel;
            }
            // save回合内发起的勒索（日志同步触发，此时SkillsFree仍是当时的真实状态）
            e.OnLog += line =>
            {
                for (int i = 0; i < 3; i++)
                    if (line.StartsWith("P" + i + " 发动【军机大臣·勒索】") && e.SkillsFree(i))
                        saveTurnExtorts++;
            };

            int guard = 0;
            while (!e.GameOver && guard++ < 20000)
            {
                int s = e.Current;
                e.ApplyAction(brains[s].ChooseAction(e, s, e.GetLegalActions(s)));
            }
            if (!e.GameOver) continue;
            finished++;
            totalExtorts += e.History.Count(l => l.Contains("发动【军机大臣·勒索】"));

            // ---- 战报解析：把事件与结果行配对 ----
            var events = new List<int[]>();          // [seat, typeNameIdx→存名, cards]
            var lastTy = new string[3];
            var pending = new int[3];                 // 当前事件已累积张数
            var hasPending = new bool[3];
            var yieldOf = new int[3];

            void Flush(int i, int winner)
            {
                if (!hasPending[i]) return;
                events.Add(new[] { i, 0, pending[i] });
                hasPending[i] = false;
            }

            foreach (var line in e.History)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (line.StartsWith("P" + i + " 发动【军机大臣·勒索】"))
                    {
                        Flush(i, e.WinnerSeat);
                        var tname = line.Substring(line.LastIndexOf('【') + 1);   // 取"指定牌型【X】"的括号
                        lastTy[i] = tname.Substring(0, tname.IndexOf('】'));
                        pending[i] = 0;
                        hasPending[i] = true;
                        break;
                    }
                }
                if (!hasPending.Any()) continue;
                for (int i = 0; i < 3; i++)
                {
                    if (!hasPending[i]) continue;
                    string tn = lastTy[i];
                    if (tn == CardDefs.Of(CardType.Hero).Name)
                    {
                        // 英雄型结果行："Px 的英雄牌被勒索打中"（x 是受害者，归属最近一个英雄事件）
                        if (line.Contains("的英雄牌被勒索打中")) { pending[i] += 1; break; }
                    }
                    else if (line.StartsWith("勒索了 ") && line.Contains("【" + tn + "】"))
                    {
                        int p1 = line.IndexOf(" 的 ") + 3, p2 = line.IndexOf(" 张【");
                        if (p1 > 2 && p2 > p1 && int.TryParse(line.Substring(p1, p2 - p1), out int n)) pending[i] += n;
                        break;
                    }
                    else if (line.Contains(" 没有【" + tn + "】")) break;
                }
            }
            for (int i = 0; i < 3; i++) Flush(i, e.WinnerSeat);

            // ---- 汇总 ----
            foreach (var ev2 in events)
            {
                int seat = ev2[0], cards = ev2[2];
                // 找到该事件牌型：从 lastTy 已不保险，改为在解析时直接存名字——用 events 附带
                yieldOf[seat] += cards;
            }
            // 重新按牌型统计（上面 events 没带名字，这里再扫一次配对）
            var evName = new List<string>();
            {
                var lt = new string[3];
                var hp = new bool[3];
                var pc = new int[3];
                void Flush2(int i2)
                {
                    if (!hp[i2]) return;
                    var ev = byType[lt[i2]];
                    ev.Times++;
                    ev.Cards += pc[i2];
                    if (pc[i2] == 0) ev.Whiff++;
                    ev.UseN++;
                    if (i2 == e.WinnerSeat) ev.UseWin++;
                    hp[i2] = false;
                }
                foreach (var line in e.History)
                {
                    for (int i = 0; i < 3; i++)
                        if (line.StartsWith("P" + i + " 发动【军机大臣·勒索】"))
                        {
                            Flush2(i);
                            var tname = line.Substring(line.LastIndexOf('【') + 1);
                            lt[i] = tname.Substring(0, tname.IndexOf('】'));
                            pc[i] = 0;
                            hp[i] = true;
                            break;
                        }
                    if (!hp.Any()) continue;
                    for (int i = 0; i < 3; i++)
                    {
                        if (!hp[i]) continue;
                        string tn = lt[i];
                        if (tn == CardDefs.Of(CardType.Hero).Name)
                        {
                            if (line.Contains("的英雄牌被勒索打中")) { pc[i]++; break; }
                        }
                        else if (line.StartsWith("勒索了 ") && line.Contains("【" + tn + "】"))
                        {
                            int p1 = line.IndexOf(" 的 ") + 3, p2 = line.IndexOf(" 张【");
                            if (p1 > 2 && p2 > p1 && int.TryParse(line.Substring(p1, p2 - p1), out int n)) pc[i] += n;
                            break;
                        }
                        else if (line.Contains(" 没有【" + tn + "】")) break;
                    }
                }
                for (int i = 0; i < 3; i++) Flush2(i);
            }

            for (int i = 0; i < 3; i++)
            {
                if (yieldOf[i] == 0 && !events.Any(x => x[0] == i)) continue;
                bool won = i == e.WinnerSeat;
                string bn = yieldOf[i] == 0 ? "用过但全空" : yieldOf[i] <= 2 ? "1-2张" : "3张以上";
                buckets[bn][0]++; if (won) buckets[bn][1]++;
            }
        }

        Console.WriteLine("对局 " + finished + " 局——勒索总次数 " + totalExtorts + "（局均 " + (1.0 * totalExtorts / finished).ToString("F2")
            + "，save回合内 " + saveTurnExtorts + " 次），基准胜率 33.3%\n");
        Console.WriteLine("牌型        次数  平均收益  空手率  使用者胜率");
        foreach (var kv in byType.OrderByDescending(kv => kv.Value.Times))
        {
            var v = kv.Value;
            if (v.Times == 0) continue;
            string avg = (1.0 * v.Cards / v.Times).ToString("F2");
            string whiff = (100.0 * v.Whiff / v.Times).ToString("F1") + "%";
            string win = v.UseN == 0 ? "—" : (100.0 * v.UseWin / v.UseN).ToString("F1") + "%(" + v.UseN + ")";
            Console.WriteLine(kv.Key.PadRight(11) + v.Times.ToString().PadRight(7) + avg.PadRight(10) + whiff.PadRight(9) + win);
        }
        Console.WriteLine("\n按整局勒索总收益分桶的使用者胜率：");
        foreach (var kv in buckets)
            if (kv.Value[0] > 0)
                Console.WriteLine("  " + kv.Key + ": " + (100.0 * kv.Value[1] / kv.Value[0]).ToString("F1") + "%(" + kv.Value[0] + "人次)");
        Console.WriteLine("（英雄型收益按'每个对手-1HP=1点'计）");
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
