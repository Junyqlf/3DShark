using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

/// <summary>
/// 普攻轮流火力场景测试（40种子）。
///   场景A【隐身测试】：0号全程不出手（只结束回合）。轮流火力下 AI-1 对 0/2 两家的攻击分布必须接近对半——
///     旧威胁索敌会几乎无视 0 号（谁装死谁隐身），轮流火力不允许。
///   场景B【轮流测试】：0号每回合固定打2号。AI-1 应优先打"本回合没人碰过的"0号（火力散布），
///     而不是无脑跟车2号。
/// 两个场景都在"两家都存活"窗口内统计 1 号的攻击目标占比，断言区间 [0.30, 0.70] / [0.40, 0.85]。
/// </summary>
public static class Program
{
    class ScriptBrain : IPlayerBrain2
    {
        readonly bool _aggressive;
        public ScriptBrain(bool aggressive) { _aggressive = aggressive; }
        public bool IsHuman { get { return false; } }
        public GameAction ChooseAction(GameEngine2 g, int seat, List<GameAction> legal)
        {
            if (_aggressive)
            {
                var atk = legal.Where(a => a.Kind == ActionKind.Attack && a.TargetSeat == 2).ToList();
                if (atk.Count > 0) return atk[0];
            }
            return legal.First(a => a.Kind == ActionKind.EndTurn);
        }
        public bool ChooseGuardIntercept(GameEngine2 g, int seat, Card drawn, int attackerSeat) { return false; }
        public int ChooseFromDiscard(GameEngine2 g, int seat, List<Card> options) { return options[0].Id; }
        public int ChooseDeathrattleTarget(GameEngine2 g, int seat, List<int> candidates) { return candidates[0]; }
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

    /// <summary>跑一批对局，统计 AI-1 在两家存活窗口内的攻击目标占比（target0 的占比）。</summary>
    static double Run(bool aggressive, int seeds, out int attacks)
    {
        int on0 = 0;
        attacks = 0;
        for (int seed = 1; seed <= seeds; seed++)
        {
            var brains = new List<IPlayerBrain2> { new ScriptBrain(aggressive) };
            for (int s = 1; s <= 2; s++) brains.Add(new AIBrain2(seed * 13 + s, s, 3));
            var e = new GameEngine2(Deck(), new[] { "脚本0", "AI-1", "AI-2" }, brains, seed * 7 + 1);
            foreach (var b in brains) if (b is AIBrain2 n) { e.OnHostile += n.NoteHostile; e.OnIntel += n.NoteIntel; }
            int guard = 8000;
            while (!e.GameOver && guard-- > 0)
            {
                if (e.Pending != null) { ResolvePending(e); continue; }
                bool window = e.Players[0].Alive && e.Players[2].Alive;   // 只在三家都在时统计
                var a = e.Brains[e.Current].ChooseAction(e, e.Current, e.GetLegalActions(e.Current));
                if (e.Current == 1 && a.Kind == ActionKind.Attack && window)
                {
                    attacks++;
                    if (a.TargetSeat == 0) on0++;
                }
                e.ApplyAction(a);
            }
        }
        return attacks > 0 ? 1.0 * on0 / attacks : 0;
    }

    public static int Main()
    {
        const int seeds = 40;
        int fails = 0;

        double hide = Run(false, seeds, out int hideAttacks);
        Console.WriteLine("场景A【隐身测试】0号全程装死：AI-1 打0号占比 " + (hide * 100).ToString("F1") + "% (" + hideAttacks + " 次攻击)");
        if (hideAttacks < 50) { Console.WriteLine("  ✗ 样本不足"); fails++; }
        else if (hide < 0.30 || hide > 0.70) { Console.WriteLine("  ✗ 超出 [30%, 70%]：装死仍能隐身或火力失衡"); fails++; }
        else Console.WriteLine("  ✓ 装死的0号照样吃到约一半火力");

        double rotate = Run(true, seeds, out int rotAttacks);
        Console.WriteLine("场景B【轮流测试】0号每回合固定打2号：AI-1 打0号占比 " + (rotate * 100).ToString("F1") + "% (" + rotAttacks + " 次攻击)");
        if (rotAttacks < 50) { Console.WriteLine("  ✗ 样本不足"); fails++; }
        else if (rotate < 0.40 || rotate > 0.85) { Console.WriteLine("  ✗ 超出 [40%, 85%]：没做到火力散布（仍在跟车）"); fails++; }
        else Console.WriteLine("  ✓ AI-1 把火力倾泻到没人碰过的0号身上，而非跟车2号");

        return fails == 0 ? 0 : 1;
    }

    static void ResolvePending(GameEngine2 e)
    {
        var pc = e.Pending;
        if (pc.Kind == ChoiceKind.GuardIntercept) e.SubmitGuardChoice(e.Brains[pc.Seat].ChooseGuardIntercept(e, pc.Seat, pc.DrawnCard, pc.AttackerSeat));
        else if (pc.Kind == ChoiceKind.PickFromDiscard) e.SubmitDiscardPick(e.Brains[pc.Seat].ChooseFromDiscard(e, pc.Seat, pc.Options));
        else
        {
            var cands = e.Players.Where(p => p.Alive && p.Seat != pc.Seat && p.Hand.Count > 0 && !e.IsProtected(pc.Seat, p.Seat))
                                 .Select(p => p.Seat).ToList();
            if (cands.Count == 0) { e.SubmitDeathrattleTarget(-1); return; }
            e.SubmitDeathrattleTarget(e.Brains[pc.Seat].ChooseDeathrattleTarget(e, pc.Seat, cands));
        }
    }
}
