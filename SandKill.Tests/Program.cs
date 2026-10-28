using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

namespace SandKill.Tests
{
    /// <summary>
    /// SandKill.Core 无头测试：不需要 Unity，双击或用 csc 编译后直接跑。
    ///
    /// 覆盖四类 invariant（不变量）：
    ///   1. 元数据表完整性 —— 每种牌都登记、文案齐全、蓝耗合理、引擎与表不漂移；
    ///   2. 规则仿真 —— AI 对 AI 打完整局，每步校验：行动合法、卡牌守恒、资源不越界、对局会结束；
    ///   3. 确定性 —— 同种子必出同结果（日志逐行一致），保证"能复现的 bug 才能修"；
    ///   4. 终局状态 —— 只剩一名存活者、胜者状态合理。
    ///
    /// 退出码：0 = 全过；1 = 有失败项。
    /// </summary>
    public static class Program
    {
        static int _passed, _failed;

        static void Check(bool cond, string name, string detail = "")
        {
            if (cond) { _passed++; }
            else
            {
                _failed++;
                Console.WriteLine("  ✗ " + name + (detail.Length > 0 ? "  | " + detail : ""));
            }
        }

        static void Section(string s) { Console.WriteLine("\n== " + s); }

        public static int Main()
        {
            MetadataTests();
            Gen1Simulation();
            Gen2Simulation();
            Determinism();
            TurnStampRegression();
            SilenceTiming();
            GreenAppleTests();
            TerroristTests();
            SynthReviveTests();
            SynthBlockTests();
            SynthDirectHitTests();
            WorkerTests();
            ShieldTests();
            Hero48Tests();
            NetProtoTests();

            Console.WriteLine("\n========================================");
            Console.WriteLine("通过 " + _passed + " 项 / 失败 " + _failed + " 项");
            return _failed == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // 1. CardDefs 元数据表
        // ------------------------------------------------------------------

        static void MetadataTests()
        {
            Section("CardDefs 元数据表");

            foreach (CardType t in Enum.GetValues(typeof(CardType)))
            {
                var def = CardDefs.Of(t);   // 表里没有会直接抛异常，跑不到下一行
                Check(def != null, "每种 CardType 都有登记", t.ToString());
                Check(!string.IsNullOrEmpty(def.Name), "中文名非空", t.ToString());
                Check(def.SkillCost >= 0 && def.SkillCost <= 5, "蓝耗在合理范围(0~5)", t.ToString());
            }

            Check(!CardDefs.Of(CardType.Worker).IsRole, "工人不是角色牌");
            Check(CardDefs.Of(CardType.Witch).SkillCost == 1, "女巫主技能(毒药)1蓝");
            Check(CardDefs.Of(CardType.Witch).SubSkillCost == 1, "女巫第二技能(解药)1蓝");

            // 蓝耗单一数据源：引擎实际读到的值必须等于表里的值（防止未来改表忘改引擎）
            Check(GameEngine2.COST_POISON == CardDefs.Of(CardType.Witch).SkillCost, "引擎毒药耗蓝 == 表");
            Check(GameEngine2.COST_ANTIDOTE == CardDefs.Of(CardType.Witch).SubSkillCost, "引擎解药耗蓝 == 表");
            Check(GameEngine2.COST_EXTORT == 1, "引擎勒索耗蓝 == 1");
            Check(GameEngine2.COST_EXORCISE == 1, "引擎驱魔耗蓝 == 1");

            Check(CardType.GrandMinister.Name() == "军机大臣", "Name() 扩展方法查表正确");

            foreach (var kv in CardDefs.All)
            {
                var d = kv.Value;
                if (d.HasActiveSkill) Check(!string.IsNullOrEmpty(d.SkillDesc), "主动技说明非空", d.Name);
                else Check(!string.IsNullOrEmpty(d.PassiveDesc), "被动说明非空", d.Name);
            }
        }

        // ------------------------------------------------------------------
        // 2. 初代引擎：AI 对 AI 仿真
        // ------------------------------------------------------------------

        static void Gen1Simulation()
        {
            Section("初代引擎：AI 对 AI 仿真（种子 1~10）");
            for (int seed = 1; seed <= 10; seed++)
            {
                var e = NewGen1(seed);
                int actions = 0;
                while (!e.GameOver && actions < 20000)
                {
                    int seat = e.Current;
                    var legal = e.GetLegalActions(seat);
                    Check(legal.Any(a => a.Kind == ActionKind.EndTurn), "合法列表总含结束回合", "seed=" + seed);

                    var act = e.Brains[seat].ChooseAction(e, seat, legal);
                    Check(legal.Any(x => x.Kind == act.Kind && x.TargetSeat == act.TargetSeat && x.SourceCardId == act.SourceCardId),
                        "AI 行动必须来自合法列表", act.ToString());

                    int before = e.TotalCards;
                    e.ApplyAction(act);
                    Check(e.TotalCards == before, "卡牌守恒", "seed=" + seed + " | " + act);
                    actions++;
                }
                Check(e.GameOver, "对局在步数上限内结束", "seed=" + seed + "，实际 " + actions + " 步");
                if (e.GameOver)
                {
                    Check(e.Players.Count(p => p.Alive) == 1, "终局只剩一名存活者", "seed=" + seed);
                    Check(e.Players[e.WinnerSeat].WorkerCount > 0, "胜者仍有工人", "seed=" + seed);
                }
            }
        }

        static GameEngine NewGen1(int seed)
        {
            var deck = new Dictionary<CardType, int>
            {
                { CardType.Worker, 12 }, { CardType.Arsenal, 1 }, { CardType.Witch, 3 },
                { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
                { CardType.Police, 4 }, { CardType.Guard, 4 }
            };
            var brains = new IPlayerBrain[]
            {
                new AIBrain(seed + 1, 0, 3), new AIBrain(seed + 2, 1, 3), new AIBrain(seed + 3, 2, 3)
            };
            var e = new GameEngine(deck, new[] { "AI-0", "AI-1", "AI-2" }, brains, seed);
            foreach (var b in brains) { var ai = b as AIBrain; if (ai != null) e.OnHostile += ai.NoteHostile; }
            return e;
        }

        // ------------------------------------------------------------------
        // 3. 二代引擎：AI 对 AI 仿真
        // ------------------------------------------------------------------

        static void Gen2Simulation()
        {
            Section("二代引擎：AI 对 AI 仿真（种子 1~10）");
            for (int seed = 1; seed <= 10; seed++)
            {
                var e = NewGen2(seed);
                int actions = 0;
                while (!e.GameOver && actions < 20000)
                {
                    int seat = e.Current;
                    var legal = e.GetLegalActions(seat);
                    Check(legal.Any(a => a.Kind == ActionKind.EndTurn), "合法列表总含结束回合", "seed=" + seed);

                    var act = ((AIBrain2)e.Brains[seat]).ChooseAction(e, seat, legal);
                    Check(legal.Any(x => x.Kind == act.Kind && x.TargetSeat == act.TargetSeat && x.SourceCardId == act.SourceCardId),
                        "AI 行动必须来自合法列表", act.ToString());

                    int before = e.TotalCards;
                    e.ApplyAction(act);
                    Check(e.TotalCards == before, "卡牌守恒(手牌+弃牌堆+牌堆+扣押+藏牌)", "seed=" + seed + " | " + act);
                    Check(e.Pending == null, "全 AI 对局不应产生真人抉择", "seed=" + seed);

                    foreach (var p in e.Players)
                    {
                        Check(p.MP <= p.MaxMP, "MP 不超上限", "seed=" + seed);
                        if (!p.Alive) Check(p.HP <= 0, "出局者 HP 已归零", "seed=" + seed);
                    }
                    actions++;
                }
                Check(e.GameOver, "对局在步数上限内结束", "seed=" + seed + "，实际 " + actions + " 步");
                if (e.GameOver)
                {
                    Check(e.Players.Count(p => p.Alive) == 1, "终局只剩一名存活者", "seed=" + seed);
                    Check(e.Players[e.WinnerSeat].HP > 0, "胜者 HP 为正", "seed=" + seed);
                }
            }
        }

        static Dictionary<CardType, int> Deck2()
        {
            return new Dictionary<CardType, int>
            {
                { CardType.Worker, 3 },
                { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
                { CardType.Police, 4 }, { CardType.Guard, 4 }, { CardType.Witch, 3 },
                { CardType.Arsenal, 1 }, { CardType.ArmedGuard, 1 }, { CardType.Minister, 4 },
                { CardType.GrandMinister, 1 }, { CardType.Warden, 3 }, { CardType.Maid, 4 },
                { CardType.GreenApple, 3 }, { CardType.Terrorist, 4 }
            };
        }

        static GameEngine2 NewGen2(int seed) { return Build2(seed, null); }

        /// <summary>按座位强制指定英雄（新英雄测试用，2026-10-14）。heroIds 传 HeroRoster 的 Id。</summary>
        static GameEngine2 NewGen2Forced(int seed, params int[] heroIds) { return Build2(seed, heroIds); }

        static GameEngine2 Build2(int seed, int[] heroIds)
        {
            var brains = new List<IPlayerBrain2>
            {
                new AIBrain2(seed + 1, 0, 3), new AIBrain2(seed + 2, 1, 3), new AIBrain2(seed + 3, 2, 3)
            };
            GameEngine2.HeroSpec[] force = null;
            if (heroIds != null) force = heroIds.Select(i => GameEngine2.HeroRoster[i]).ToArray();
            var e = new GameEngine2(Deck2(), new[] { "AI-0", "AI-1", "AI-2" }, brains, seed, force, new[] { 0, 0, 0 });   // 调度券清零：专项测试另建（2026-10-16），保证旧断言轨迹不变
            foreach (var b in brains) { var ai = b as AIBrain2; if (ai != null) { e.OnHostile += ai.NoteHostile; e.OnIntel += ai.NoteIntel; } }
            return e;
        }

        // ------------------------------------------------------------------
        // 4. 确定性：同种子 → 同结果（复现 bug 的前提）
        // ------------------------------------------------------------------

        static void Determinism()
        {
            Section("确定性：同种子同结果（种子 1~5，两代各一对）");
            for (int seed = 1; seed <= 5; seed++)
            {
                var a = NewGen1(seed); var b = NewGen1(seed);
                RunToEnd(a); RunToEnd(b);
                Check(a.WinnerSeat == b.WinnerSeat, "初代胜者一致", "seed=" + seed);
                Check(a.TurnNumber == b.TurnNumber && a.History.Count == b.History.Count, "初代轮数/日志长度一致", "seed=" + seed);
                Check(!a.History.Where((m, i) => m != b.History[i]).Any(), "初代日志逐行一致", "seed=" + seed);

                var c = NewGen2(seed); var d = NewGen2(seed);
                RunToEnd(c); RunToEnd(d);
                Check(c.WinnerSeat == d.WinnerSeat, "二代胜者一致", "seed=" + seed);
                Check(c.TurnNumber == d.TurnNumber && c.History.Count == d.History.Count, "二代轮数/日志长度一致", "seed=" + seed);
            }
        }

        // ------------------------------------------------------------------
        // 5. 守卫"无法选取"状态回归（2026-10-15 改版）：牌不离手、到期自动清除、
        //    状态期间不参与一切针对手牌的判定（回合戳机制保证状态不会漏清）
        // ------------------------------------------------------------------

        static void TurnStampRegression()
        {
            Section("守卫护牌：无法选取状态留在手牌、到期自动清除");
            for (int seed = 1; seed <= 5; seed++)
            {
                var e = NewGen2(seed);
                var me = e.Players[0];

                // 直接发三张测试牌（避开发牌随机性）：带刀侍卫A（护牌对象）+ 守卫W + 大臣M（贿赂判定用）
                var a = new Card(9001, CardType.ArmedGuard);
                var w = new Card(9002, CardType.Warden);
                var m = new Card(9003, CardType.Minister);
                me.Hand.Add(a); me.Hand.Add(w); me.Hand.Add(m);

                e.ApplyAction(new GameAction { Kind = ActionKind.Hide, SourceCardId = w.Id, SecondCardId = a.Id });
                Check(me.Hand.Contains(a), "被护的牌仍留在手牌", "seed=" + seed);
                Check(e.IsGuardProtected(a), "被护的牌处于无法选取状态", "seed=" + seed);
                Check(!e.GetLegalActions(0).Any(x => x.Kind == ActionKind.Hide && x.SecondCardId == a.Id),
                    "已在状态中的牌不能重复施加", "seed=" + seed);
                Check(!e.GetLegalActions(0).Any(x => x.Kind == ActionKind.Bribe && x.SecondCardId == a.Id),
                    "被护的牌不参与贿赂给出物判定", "seed=" + seed);

                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                int guard = 0;
                while (e.Current != 0 && !e.GameOver && guard++ < 100)
                {
                    int seat = e.Current;
                    e.ApplyAction(((AIBrain2)e.Brains[seat]).ChooseAction(e, seat, e.GetLegalActions(seat)));
                }
                if (e.GameOver) continue;   // 极端情况：这一圈有人出局打完，跳过本种子

                Check(me.Hand.Contains(a), "被护的牌不会离手", "seed=" + seed);
                Check(!e.IsGuardProtected(a), "自己下回合开始状态已到期清除", "seed=" + seed);
                // 2026-10-15 用户裁定两条：防连守（状态到期≠守过记忆清除，本回合仍不能护同一张）+ 不能护守卫自己
                Check(!e.GetLegalActions(0).Any(x => x.Kind == ActionKind.Hide && x.SecondCardId == a.Id),
                    "防连守：状态到期当回合仍不能护同一张", "seed=" + seed);
                Check(!e.GetLegalActions(0).Any(x => x.Kind == ActionKind.Hide && x.SourceCardId == w.Id && x.SecondCardId == w.Id),
                    "守卫不能护自己（同一张牌不能既发动又当目标）", "seed=" + seed);
            }
        }

        // ------------------------------------------------------------------
        // 6. 沉默持续时机：police station 沉默敌方整个"下一回合"，该回合结束即解除
        // ------------------------------------------------------------------

        static void SilenceTiming()
        {
            Section("沉默时机：覆盖被沉默者的整个回合，回合结束解除");
            for (int seed = 1; seed <= 5; seed++)
            {
                var e = NewGen2(seed);
                int ps = -1;
                for (int i = 0; i < 3; i++) if (e.HeroOf(i).Id == 1 && e.Players[i].Alive) ps = i;
                if (ps < 0) continue;

                int guard = 0;
                while (e.Current != ps && !e.GameOver && guard++ < 200)
                {
                    int seat = e.Current;
                    e.ApplyAction(((AIBrain2)e.Brains[seat]).ChooseAction(e, seat, e.GetLegalActions(seat)));
                }
                if (e.GameOver) continue;

                e.ApplyAction(new GameAction { Kind = ActionKind.HeroPower });
                int victim = -1;
                for (int i = 0; i < 3; i++) if (i != ps && e.Players[i].Alive) { victim = i; break; }
                Check(victim >= 0 && e.IsSilenced(victim), "发动后敌人被沉默", "seed=" + seed);

                guard = 0;
                while (e.Current != victim && !e.GameOver && guard++ < 200)
                {
                    int seat = e.Current;
                    e.ApplyAction(((AIBrain2)e.Brains[seat]).ChooseAction(e, seat, e.GetLegalActions(seat)));
                }
                if (e.GameOver) continue;

                Check(e.IsSilenced(victim), "被沉默者的整个回合都处于沉默中", "seed=" + seed);
                var legal = e.GetLegalActions(victim);
                Check(legal.Any(a => a.Kind == ActionKind.Attack) || legal.Any(a => a.Kind == ActionKind.EndTurn),
                    "沉默中普攻/结束回合仍可用", "seed=" + seed);
                Check(!legal.Any(a => a.SourceCardId >= 0 || a.Kind == ActionKind.HeroPower),
                    "沉默中没有任何技能行动", "seed=" + seed);

                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                Check(!e.IsSilenced(victim), "被沉默者的回合结束后沉默解除", "seed=" + seed);
            }
        }

        // ------------------------------------------------------------------
        // 7. 青苹果：受伤倒数→吃掉回血；被攻击抽中→被抢走（不进弃牌堆）
        // ------------------------------------------------------------------

        static void GreenAppleTests()
        {
            Section("青苹果：受伤倒数→吃掉回血；被抽中→被抢走");
            int stealSeen = 0, countdownSeen = 0, eatenSeen = 0;
            for (int seed = 1; seed <= 30; seed++)
            {
                var e = NewGen2(seed);
                var p1 = e.Players[1];
                // 构造：1号手牌只留英雄牌+一张青苹果（其余移入弃牌堆），排除挡刀/免疫干扰
                foreach (var c in p1.Hand.Where(c => c.Type != CardType.Hero).ToList()) { p1.Hand.Remove(c); e.Discard.Add(c); }
                var apple = new Card(9100 + seed, CardType.GreenApple);
                p1.Hand.Add(apple);
                int cardsBefore = e.TotalCards;

                e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 1 });
                Check(e.TotalCards == cardsBefore, "攻击后卡牌守恒", "seed=" + seed);

                if (e.Players[0].Hand.Contains(apple))
                {
                    // 青苹果被抽中 → 被攻击者抢走：不进弃牌堆、倒数取消
                    stealSeen++;
                    Check(apple.AppleTimer == -1, "被抢走的青苹果倒数取消", "seed=" + seed);
                    Check(!e.Discard.Contains(apple), "被抢走的青苹果不进弃牌堆", "seed=" + seed);
                }
                else if (apple.AppleTimer == 2)
                {
                    // 英雄牌被抽中 → 1号受伤 → 青苹果开始倒数
                    countdownSeen++;
                    Check(p1.HP == p1.MaxHP - 1, "英雄被抽中掉1点HP", "seed=" + seed);
                    int hpAfterHurt = p1.HP;
                    // 空过到1号的第2个回合开始（倒数2→1→0，吃掉回血）
                    int guard = 0;
                    while (p1.OwnTurnCount < 2 && !e.GameOver && guard++ < 20)
                        e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                    if (apple.AppleTimer > 0) { Check(false, "两回合后倒数应已走完", "seed=" + seed + " timer=" + apple.AppleTimer); continue; }
                    if (e.Discard.Contains(apple) && p1.HP == hpAfterHurt + 1) eatenSeen++;
                    Check(e.Discard.Contains(apple), "倒数结束的青苹果被吃掉进弃牌堆", "seed=" + seed);
                    Check(p1.HP == hpAfterHurt + 1, "吃掉青苹果回复1点HP", "seed=" + seed + " HP=" + p1.HP);
                }
                else Check(false, "青苹果应要么被抢走要么开始倒数", "seed=" + seed);
            }
            Check(stealSeen + countdownSeen == 30, "每次攻击都有明确去向", "steal=" + stealSeen + " countdown=" + countdownSeen);
            Check(stealSeen >= 5 && countdownSeen >= 5, "两种分支都应被覆盖（各≥5/30）", "steal=" + stealSeen + " countdown=" + countdownSeen);
            Check(eatenSeen >= 3, "吃掉回血分支应被覆盖（≥3/30）", "eaten=" + eatenSeen);
        }

        // ------------------------------------------------------------------
        // 8. 恐怖分子（2026-10-13 改版）：主动【安放炸弹】——2回合后爆炸，对所有敌方英雄各造成1点伤害，
        //    恐怖分子自己同归于尽。期间被警察/PS沉默=拆弹+返还cd；恐怖分子阵亡=哑弹；
        //    被大臣贿赂送人也不改目标（仍以安放者的敌我划线）。
        // ------------------------------------------------------------------

        static void TerroristTests()
        {
            Section("恐怖分子：安放炸弹（2026-10-13 改版：无来源随机攻击，非锁头英雄伤害）");
            int boomSeen = 0, defuseSeen = 0, fizzleSeen = 0, bribeSeen = 0, resumeSeen = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                // ---- A. 安放→2回合后爆炸：冲击波=对每个敌手随机抽1张手牌（等同普攻），不锁英雄 ----
                // 1号只剩英雄牌（必被抽中→-1HP后回手）；2号只有一张女巫（被炸进弃牌堆，不掉HP）
                var e = NewGen2(seed);
                e.SideDeck.Clear();   // 清空牌堆：防止敌手回合开始补牌干扰固定手牌
                var p0 = e.Players[0];
                foreach (var c in p0.Hand.Where(c => c.Type != CardType.Hero).ToList())
                { p0.Hand.Remove(c); e.Discard.Add(c); }
                var terror = new Card(9200 + seed, CardType.Terrorist);
                p0.Hand.Add(terror);
                p0.MP = p0.MaxMP;
                var p1 = e.Players[1];
                var hero1 = p1.Hand.FirstOrDefault(c => c.Type == CardType.Hero);
                p1.Hand.Clear(); if (hero1 != null) p1.Hand.Add(hero1);
                var p2 = e.Players[2];
                p2.Hand.Clear();
                var witch = new Card(9210 + seed, CardType.Witch);
                p2.Hand.Add(witch);
                int hp1 = p1.HP, hp2 = p2.HP;

                e.ApplyAction(new GameAction { Kind = ActionKind.BombPlant, SourceCardId = terror.Id });
                Check(terror.BombActive, "安放后炸弹进入倒数", "seed=" + seed);
                Check(p0.MP == p0.MaxMP - GameEngine2.COST_BOMB, "安放消耗1蓝", "seed=" + seed);
                Check(e.GetLegalActions(0).All(a => a.Kind != ActionKind.BombPlant),
                    "同一张恐怖分子倒数中不能重复安放", "seed=" + seed);

                // 空过两圈到0号的第2个回合开始（OwnTurnCount 1→2，炸弹到期）
                int guard = 0;
                while (p0.OwnTurnCount < 3 && !e.GameOver && guard++ < 30)
                    e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });

                if (!e.GameOver && p0.Alive)
                {
                    Check(!terror.BombActive, "爆炸后炸弹状态清除", "seed=" + seed);
                    Check(e.Discard.Contains(terror), "恐怖分子与敌同归于尽进弃牌堆", "seed=" + seed);
                    Check(e.Discard.Contains(witch), "冲击波：敌方手牌被随机抽中炸进弃牌堆", "seed=" + seed);
                    Check(p1.HP == hp1 - 1 && p2.HP == hp2,
                        "锁手牌而非锁英雄：仅被抽中英雄牌的敌手掉HP", "seed=" + seed + " hp=" + p1.HP + "/" + p2.HP);
                    Check(hero1 != null && p1.Hand.Contains(hero1), "英雄牌被抽中后回到手牌", "seed=" + seed);
                    Check(e.History.Any(m => m.Contains("炸弹引爆")), "爆炸已播报", "seed=" + seed);
                    Check(e.History.Any(m => m.Contains("冲击波")), "冲击波播报", "seed=" + seed);
                    boomSeen++;
                }

                // ---- B. 警察封印=拆弹+返还cd（蓝不返还） ----
                // 轮次：0号安放 → 1号警察封印（随机非英雄牌必中恐怖分子）→ 空过到封印解除后再次可安放
                var e2 = NewGen2(seed);
                var q0 = e2.Players[0];
                foreach (var c in q0.Hand.Where(c => c.Type != CardType.Hero).ToList())
                { q0.Hand.Remove(c); e2.SideDeck.Add(c); }
                var terror2 = new Card(9300 + seed, CardType.Terrorist);
                q0.Hand.Add(terror2);
                q0.MP = q0.MaxMP;
                e2.ApplyAction(new GameAction { Kind = ActionKind.BombPlant, SourceCardId = terror2.Id });
                int mpAfterPlant = q0.MP;
                e2.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 轮到1号
                e2.Players[1].Hand.Add(new Card(9800 + seed, CardType.Police));
                e2.ApplyAction(new GameAction { Kind = ActionKind.Seize, TargetSeat = 0, SourceCardId = 9800 + seed });
                Check(!terror2.BombActive, "警察封印：炸弹被拆除（倒数中断）", "seed=" + seed);
                Check(q0.MP == mpAfterPlant, "拆弹不返还蓝", "seed=" + seed);
                Check(e2.History.Any(m => m.Contains("拆除")), "拆弹已播报", "seed=" + seed);
                // 空过到0号的第3个回合（封印在0号第2个回合结束时解除），返还cd应已生效
                int g2 = 0;
                while (!(e2.Current == 0 && q0.OwnTurnCount >= 3) && !e2.GameOver && g2++ < 15)
                    e2.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                if (!e2.GameOver)
                    Check(e2.GetLegalActions(0).Any(a => a.Kind == ActionKind.BombPlant && a.SourceCardId == terror2.Id),
                        "拆弹返还cd：封印过期后可再次安放", "seed=" + seed);
                defuseSeen++;

                // ---- C. 恐怖分子阵亡=哑弹（效果自然消失，不退蓝） ----
                var e3 = NewGen2(seed);
                var r0 = e3.Players[0];
                foreach (var c in r0.Hand.Where(c => c.Type != CardType.Hero).ToList())
                { r0.Hand.Remove(c); e3.SideDeck.Add(c); }
                var terror3 = new Card(9400 + seed, CardType.Terrorist);
                r0.Hand.Add(terror3);
                r0.MP = r0.MaxMP;
                e3.ApplyAction(new GameAction { Kind = ActionKind.BombPlant, SourceCardId = terror3.Id });
                r0.Hand.Remove(terror3);   // 模拟恐怖分子先于爆炸阵亡（被处决/被藏前抽走等）
                e3.Discard.Add(terror3);
                int hp1c = e3.Players[1].HP, hp2c = e3.Players[2].HP;
                guard = 0;
                while (r0.OwnTurnCount < 3 && !e3.GameOver && guard++ < 30)
                    e3.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                Check(e3.History.Any(m => m.Contains("哑弹")), "恐怖分子阵亡：炸弹成哑弹", "seed=" + seed);
                Check(e3.Players[1].HP == hp1c && e3.Players[2].HP == hp2c,
                    "哑弹不造成任何伤害", "seed=" + seed);
                fizzleSeen++;

                // ---- D. 被大臣贿赂送人也不改目标：仍以安放者的敌我划线（受赠者照样挨炸） ----
                var e4 = NewGen2(seed);
                e4.SideDeck.Clear();
                var s0 = e4.Players[0];
                foreach (var c in s0.Hand.Where(c => c.Type != CardType.Hero).ToList())
                { s0.Hand.Remove(c); e4.Discard.Add(c); }
                var terror4 = new Card(9500 + seed, CardType.Terrorist);
                var minister4 = new Card(9501 + seed, CardType.Minister);
                s0.Hand.Add(terror4);
                s0.Hand.Add(minister4);
                s0.MP = s0.MaxMP;
                e4.ApplyAction(new GameAction { Kind = ActionKind.BombPlant, SourceCardId = terror4.Id });
                e4.ApplyAction(new GameAction { Kind = ActionKind.Bribe, TargetSeat = 1,
                    SourceCardId = minister4.Id, SecondCardId = terror4.Id });
                Check(e4.Players[1].Hand.Contains(terror4), "恐怖分子被贿赂送人", "seed=" + seed);
                Check(terror4.BombActive && terror4.BombPlanter == 0, "炸弹仍在倒数且目标未更换", "seed=" + seed);
                // 爆炸前固定双方手牌：受赠者1号手里留炸弹+一张女巫，2号一张女巫——都应被炸进弃牌堆
                var p1d = e4.Players[1];
                p1d.Hand.Clear();
                var witchB = new Card(9510 + seed, CardType.Witch);
                p1d.Hand.Add(witchB);
                p1d.Hand.Add(terror4);
                var p2d = e4.Players[2];
                p2d.Hand.Clear();
                var witchC = new Card(9511 + seed, CardType.Witch);
                p2d.Hand.Add(witchC);
                int hp0d = e4.Players[0].HP;
                guard = 0;
                while (s0.OwnTurnCount < 3 && !e4.GameOver && guard++ < 30)
                    e4.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                if (!e4.GameOver && s0.Alive)
                {
                    Check(e4.Discard.Contains(terror4), "爆炸：恐怖分子进弃牌堆（在受赠者手里也照爆）", "seed=" + seed);
                    Check(e4.Discard.Contains(witchB) && e4.Discard.Contains(witchC),
                        "爆炸仍以安放者的敌我划线（受赠者与另一敌手都被炸）", "seed=" + seed);
                    Check(e4.Players[0].HP == hp0d, "安放者自己不受爆炸影响", "seed=" + seed);
                    bribeSeen++;
                }
            }
            Check(boomSeen >= 13, "爆炸分支全种子覆盖", "boom=" + boomSeen);
            Check(defuseSeen == 20, "警察拆弹全种子覆盖", "defuse=" + defuseSeen);
            Check(fizzleSeen == 20, "哑弹分支全种子覆盖", "fizzle=" + fizzleSeen);
            Check(bribeSeen >= 13, "贿赂不改目标分支覆盖", "bribe=" + bribeSeen);

            // ---- E. police station 沉默=拆弹（找0号英雄为PS的种子） ----
            int psSeed = -1;
            for (int seed = 1; seed <= 50; seed++)
            {
                var probe = NewGen2(seed);
                if (probe.HeroOf(1).Id == 1) { psSeed = seed; break; }
            }
            Check(psSeed > 0, "找到PS英雄在1号位的种子", "seed=" + psSeed);
            if (psSeed > 0)
            {
                var e5 = NewGen2(psSeed);
                var t0 = e5.Players[0];
                foreach (var c in t0.Hand.Where(c => c.Type != CardType.Hero).ToList())
                { t0.Hand.Remove(c); e5.SideDeck.Add(c); }
                var terror5 = new Card(9900, CardType.Terrorist);
                t0.Hand.Add(terror5);
                t0.MP = t0.MaxMP;
                e5.ApplyAction(new GameAction { Kind = ActionKind.BombPlant, SourceCardId = terror5.Id });
                e5.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 轮到1号
                Check(e5.Current == 1, "轮到1号（PS英雄）行动", "cur=" + e5.Current);
                e5.ApplyAction(new GameAction { Kind = ActionKind.HeroPower });
                Check(!terror5.BombActive, "police station 沉默：安放者的炸弹被拆除", "seed=" + psSeed);
                Check(e5.History.Any(m => m.Contains("police station")), "PS发动已播报", "seed=" + psSeed);
            }

            // ---- F. 断点续炸：冲击波炸出人类座位的亡语抉择时暂停，提交后续炸剩余敌手 ----
            for (int seed = 1; seed <= 5; seed++)
            {
                var brains = new List<IPlayerBrain2>
                {
                    new HumanBrain2(), new HumanBrain2(), new AIBrain2(seed + 3, 2, 3)
                };
                var e6 = new GameEngine2(BlockTestDeck(), new[] { "H0", "H1", "AI2" }, brains, seed,
                    new[] { GameEngine2.HeroRoster[0], GameEngine2.HeroRoster[0], GameEngine2.HeroRoster[0] },
                    new[] { 0, 0, 0 });   // 强制医师+调度券清零：排除牺牲英雄（牧师挡刀身体走另一条结算，干扰Pending断言）
                e6.SideDeck.Clear();   // 清空牌堆：保证固定手牌不被回合开始补牌干扰
                var x0 = e6.Players[0];
                foreach (var c in x0.Hand.Where(c => c.Type != CardType.Hero).ToList())
                { x0.Hand.Remove(c); e6.Discard.Add(c); }
                var terror6 = new Card(9950 + seed, CardType.Terrorist);
                x0.Hand.Add(terror6);
                x0.MP = x0.MaxMP;
                // 1号（人类）只有一张牧师；弃牌堆预置一张女巫供亡语回收
                var x1 = e6.Players[1];
                x1.Hand.Clear();
                var priest6 = new Card(9960 + seed, CardType.Priest);
                x1.Hand.Add(priest6);
                var fodder = new Card(9970 + seed, CardType.Witch);
                e6.Discard.Add(fodder);
                // 2号（AI）只剩英雄牌：续炸后必被抽中 → -1HP
                var x2 = e6.Players[2];
                var hero2 = x2.Hand.FirstOrDefault(c => c.Type == CardType.Hero);
                x2.Hand.Clear(); if (hero2 != null) x2.Hand.Add(hero2);
                int hp2f = x2.HP;

                e6.ApplyAction(new GameAction { Kind = ActionKind.BombPlant, SourceCardId = terror6.Id });
                int gf = 0;
                while (x0.OwnTurnCount < 3 && !e6.GameOver && gf++ < 30)
                    e6.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });

                Check(e6.Pending != null && e6.Pending.Kind == ChoiceKind.PickFromDiscard,
                    "冲击波炸出牧师亡语：真人弹出回收抉择（队列暂停）", "seed=" + seed);
                Check(e6.Discard.Contains(priest6), "牧师被炸进弃牌堆", "seed=" + seed);
                Check(x2.HP == hp2f, "暂停中：2号尚未被炸", "seed=" + seed);
                if (e6.Pending == null) continue;
                int pickId = e6.Pending.Options[0].Id;   // 回收选项按牌型去重，选项[0]不一定是预置的女巫
                e6.SubmitDiscardPick(pickId);
                Check(x1.Hand.Any(c => c.Id == pickId), "提交回收：1号拿回牌", "seed=" + seed);
                Check(x2.HP == hp2f - 1, "断点续炸：2号随后被炸（英雄被抽中-1HP）", "seed=" + seed);
                Check(e6.Pending == null, "队列炸完后无遗留抉择", "seed=" + seed);
                resumeSeen++;
            }
            Check(resumeSeen == 5, "断点续炸全种子覆盖", "resume=" + resumeSeen);
        }

        // ------------------------------------------------------------------
        // 9. 合成牌复活平衡 ReviveSynthStyle（2026-10-08 用户设计）：
        //    0=现行（可复活合成牌，生命保留）/ 1=禁止复活合成牌 / 2=复活后只剩1点生命
        //    覆盖牧师亡语回收渠道（女巫解药/女祭司复活走同一 DiscardOptions 过滤）
        // ------------------------------------------------------------------
        static void SynthReviveTests()
        {
            Section("合成牌复活平衡 ReviveSynthStyle（0现行/1禁复活/2复活1点生命）");
            int covered0 = 0, covered1 = 0, covered2 = 0;
            for (int seed = 1; seed <= 30; seed++)
            {
                for (int style = 0; style <= 2; style++)
                {
                    var e = NewGen2(seed);
                    var p1 = e.Players[1];
                    if (e.HeroOf(1).PriestSacrifice) continue;   // 牺牲英雄牧师变挡刀身体，走另一条结算，跳过
                    p1.Hand.Clear();   // 直接清空，保证弃牌堆只有圣骑士一个回收选项
                    var paladin = new Card(9500 + seed * 10 + style, CardType.Paladin) { SynthHP = 2 };
                    e.Discard.Add(paladin);
                    p1.Hand.Add(new Card(9600 + seed * 10 + style, CardType.Priest));
                    e.ReviveSynthStyle[1] = style;

                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 1 });
                    if (style == 1)
                    {
                        Check(e.Discard.Contains(paladin), "方式1：合成牌禁止复活（留在弃牌堆）",
                            "style=" + style + " seed=" + seed);
                        Check(!p1.Hand.Contains(paladin), "方式1：合成牌没回手", "style=" + style + " seed=" + seed);
                        covered1++;
                    }
                    else
                    {
                        Check(p1.Hand.Contains(paladin), "方式" + style + "：合成牌可复活（0=现行/2=减生命）",
                            "style=" + style + " seed=" + seed);
                        if (style == 0) { Check(paladin.SynthHP == 2, "方式0：复活后生命保留2点", "seed=" + seed); covered0++; }
                        if (style == 2) { Check(paladin.SynthHP == 1, "方式2：复活后生命只剩1点", "seed=" + seed); covered2++; }
                    }
                }
            }
            Check(covered0 >= 25 && covered1 >= 25 && covered2 >= 25,
                "三种复活方式都应被覆盖（各≥25/30，牺牲英雄池占跳过额度）",
                "covered=" + covered0 + "/" + covered1 + "/" + covered2);
        }

        // ------------------------------------------------------------------
        // 10. 合成牌挡刀 2 点生命结算（2026-10-08 用户裁定）：
        //    女武神/圣骑士每挡1刀扣1点 SynthHP，>0 回到手牌；耗尽才进弃牌堆（圣骑士耗尽触发亡语）。
        //    回归背景：旧版挡完1刀直接进弃牌堆，2HP 设计形同虚设。
        // ------------------------------------------------------------------
        static void SynthBlockTests()
        {
            Section("合成牌挡刀2HP：挡1刀扣1生命回手，耗尽才进弃牌堆");
            foreach (CardType synth in new[] { CardType.Valkyrie, CardType.Paladin })
            {
                int seen = 0;
                for (int seed = 1; seed <= 10; seed++)
                {
                    // 1号用真人脑：被攻击弹Pending后由测试强制指定用合成牌挡刀（AI挡刀纪律只挡≥8价值，会拒挡5~6价值的合成牌）
                    var deck = new Dictionary<CardType, int>
                    {
                        { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
                        { CardType.Police, 4 }, { CardType.Guard, 4 }, { CardType.Witch, 3 },
                        { CardType.Arsenal, 1 }, { CardType.ArmedGuard, 1 }, { CardType.Minister, 4 },
                        { CardType.GrandMinister, 1 }, { CardType.Warden, 3 }, { CardType.Maid, 4 },
                        { CardType.GreenApple, 3 }, { CardType.Terrorist, 4 }
                    };
                    var brains = new List<IPlayerBrain2>
                    {
                        new AIBrain2(seed + 1, 0, 3), new HumanBrain2(), new AIBrain2(seed + 3, 2, 3)
                    };
                    var e = new GameEngine2(deck, new[] { "AI-0", "HUMAN-1", "AI-2" }, brains, seed, null, new[] { 0, 0, 0 });   // 调度券清零：固定手牌断言不受开局调度干扰
                    var p1 = e.Players[1];
                    p1.Hand.Clear();   // 保证必抽中合成牌
                    var body = new Card(97000 + (int)synth * 100 + seed, synth) { SynthHP = 2 };
                    p1.Hand.Add(body);

                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 1 });   // 第1刀
                    Check(e.Pending != null && e.Pending.Kind == ChoiceKind.GuardIntercept, synth.Name() + "：真人座位弹出挡刀抉择", "seed=" + seed);
                    if (e.Pending == null) continue;
                    e.SubmitGuardChoice(true, body.Id);
                    Check(body.SynthHP == 1, synth.Name() + "：挡第1刀后剩1点生命", "seed=" + seed);
                    Check(p1.Hand.Contains(body), synth.Name() + "：挡第1刀后回到手牌（未消耗）", "seed=" + seed);
                    Check(!e.Discard.Contains(body), synth.Name() + "：挡第1刀不进弃牌堆", "seed=" + seed);

                    // 普攻每回合1次：空过一回合再到0号的回合打第2刀
                    for (int i = 0; i < 3 && !e.GameOver; i++)
                        e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 1 });   // 第2刀
                    if (e.Pending != null) e.SubmitGuardChoice(true, body.Id);
                    Check(body.SynthHP == 0, synth.Name() + "：挡第2刀后生命归零", "seed=" + seed);
                    Check(e.Discard.Contains(body), synth.Name() + "：生命耗尽进入弃牌堆", "seed=" + seed);
                    if (synth == CardType.Paladin)
                        Check(e.History.Any(m => m.Contains("圣骑士") && m.Contains("亡语")),
                            "圣骑士：耗尽时触发亡语", "seed=" + seed);
                    seen++;
                }
                Check(seen == 10, synth.Name() + " 2HP挡刀全种子覆盖", "seen=" + seen);
            }
        }

        // ------------------------------------------------------------------
        // 12. 合成牌被攻击"直接抽中"（2026-10-13 修复文字与效果不符）：
        //    描述承诺2点生命，但旧引擎在 ResolveDrawn 走通用弃牌路径，被直接抽中当场死亡。
        //    现改为：未沉默/封印时耗1点生命回手，耗尽才进弃牌堆（圣骑士触发亡语）；
        //    被沉默/封印则与普通牌一样直接阵亡（亡语不触发）。
        // ------------------------------------------------------------------
        static Dictionary<CardType, int> BlockTestDeck()
        {
            return new Dictionary<CardType, int>
            {
                { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
                { CardType.Police, 4 }, { CardType.Guard, 4 }, { CardType.Witch, 3 },
                { CardType.Arsenal, 1 }, { CardType.ArmedGuard, 1 }, { CardType.Minister, 4 },
                { CardType.GrandMinister, 1 }, { CardType.Warden, 3 }, { CardType.Maid, 4 },
                { CardType.GreenApple, 3 }, { CardType.Terrorist, 4 }
            };
        }

        static void SynthDirectHitTests()
        {
            Section("合成牌被直接抽中：耗1生命回手；耗尽进弃牌堆+亡语；被封印直接死");
            foreach (CardType synth in new[] { CardType.Valkyrie, CardType.Paladin })
            {
                int seen = 0;
                for (int seed = 1; seed <= 10; seed++)
                {
                    var brains = new List<IPlayerBrain2>
                    {
                        new HumanBrain2(), new HumanBrain2(), new AIBrain2(seed + 3, 2, 3)
                    };
                    var e = new GameEngine2(BlockTestDeck(), new[] { "HUMAN-0", "HUMAN-1", "AI-2" }, brains, seed, null, new[] { 0, 0, 0 });   // 调度券清零：固定手牌断言不受开局调度干扰
                    var p1 = e.Players[1];
                    p1.Hand.Clear();   // 保证必抽中合成牌
                    var body = new Card(98000 + (int)synth * 100 + seed, synth) { SynthHP = 2 };
                    p1.Hand.Add(body);

                    // 第1次直接抽中：耗1点生命，撑着回到手牌
                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 1 });
                    Check(e.Pending != null && e.Pending.Kind == ChoiceKind.GuardIntercept, synth.Name() + "：真人座位弹出挡刀抉择", "seed=" + seed);
                    if (e.Pending == null) continue;
                    e.SubmitGuardChoice(false, body.Id);   // 拒绝挡刀 = 直接抽中
                    Check(body.SynthHP == 1, synth.Name() + "：直接抽中耗1点生命（剩1）", "seed=" + seed);
                    Check(p1.Hand.Contains(body), synth.Name() + "：直接抽中后回到手牌", "seed=" + seed);
                    Check(!e.Discard.Contains(body), synth.Name() + "：直接抽中不进弃牌堆", "seed=" + seed);

                    // 空过一回合再打第2刀：生命耗尽进弃牌堆
                    for (int i = 0; i < 3 && !e.GameOver; i++)
                        e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 1 });
                    if (e.Pending != null) e.SubmitGuardChoice(false, body.Id);
                    Check(body.SynthHP == 0, synth.Name() + "：第2次直接抽中生命归零", "seed=" + seed);
                    Check(e.Discard.Contains(body), synth.Name() + "：生命耗尽进入弃牌堆", "seed=" + seed);
                    if (synth == CardType.Paladin)
                        Check(e.History.Any(m => m.Contains("圣骑士") && m.Contains("亡语")),
                            "圣骑士：直接抽中耗尽也触发亡语", "seed=" + seed);
                    seen++;
                }
                Check(seen == 10, synth.Name() + " 直接抽中全种子覆盖", "seen=" + seen);
            }

            // 封印分支：警察封住合成牌后被攻击抽中 → 直接阵亡，不耗生命、不触发亡语
            int silSeen = 0;
            for (int seed = 1; seed <= 10; seed++)
            {
                var brains = new List<IPlayerBrain2>
                {
                    new HumanBrain2(), new HumanBrain2(), new AIBrain2(seed + 3, 2, 3)
                };
                var e = new GameEngine2(BlockTestDeck(), new[] { "HUMAN-0", "HUMAN-1", "AI-2" }, brains, seed, null, new[] { 0, 0, 0 });   // 调度券清零：固定手牌断言不受开局调度干扰
                var p0 = e.Players[0];
                p0.Hand.RemoveAll(c => c.Type != CardType.Hero);
                var cop = new Card(98500 + seed, CardType.Police);
                p0.Hand.Add(cop);
                var p1 = e.Players[1];
                p1.Hand.Clear();
                var body = new Card(98600 + seed, CardType.Paladin) { SynthHP = 2 };
                p1.Hand.Add(body);

                e.ApplyAction(new GameAction { Kind = ActionKind.Seize, TargetSeat = 1, SourceCardId = cop.Id });
                Check(e.CardSilenced(body), "警察封印命中合成牌（其手牌只有它）", "seed=" + seed);
                e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 1 });
                if (e.Pending != null) e.SubmitGuardChoice(false, body.Id);
                Check(body.SynthHP == 2, "被封印：生命不消耗（仍2点）", "seed=" + seed);
                Check(e.Discard.Contains(body), "被封印：直接阵亡进弃牌堆", "seed=" + seed);
                Check(!e.History.Any(m => m.Contains("】亡语触发")), "被封印：亡语不触发", "seed=" + seed);
                silSeen++;
            }
            Check(silSeen == 10, "封印直接阵亡分支全种子覆盖", "seen=" + silSeen);
        }

        // ------------------------------------------------------------------
        // 11. 工人（2026-10-13 改版）：进2代牌库×3，效果=行动点上限+1（多张不叠加）。
        //    旧"合成奖励工人+工人护盾"方案废止：合成奖励固定回抽1张牌；HP归零即出局（不再看工人）。
        // ------------------------------------------------------------------
        static void WorkerTests()
        {
            Section("工人：行动点+1（2026-10-13 回归牌库）+ 护盾废止");
            int mpSeen = 0, stackSeen = 0;
            for (int seed = 1; seed <= 10; seed++)
            {
                var e = NewGen2(seed);
                var p0 = e.Players[0];
                // 先把0号手牌里随机发到的工人挪走，测干净基线
                foreach (var c in p0.Hand.Where(c => c.Type == CardType.Worker).ToList())
                { p0.Hand.Remove(c); e.SideDeck.Add(c); }
                var w1 = new Card(96000 + seed * 10, CardType.Worker);
                p0.Hand.Add(w1);
                var hero = e.HeroOf(0);
                // 基线 = 英雄MP + 攻城锤前2回合加成（当前OwnTurnCount=1，在窗口内则+1）
                int BaseMp(int ownTurnCount)
                {
                    return hero.MP + (hero.EarlyMpBonus > 0 && ownTurnCount <= hero.ExtraAttackEarly ? hero.EarlyMpBonus : 0);
                }

                // 手牌有1张工人 → 新回合 MaxMP = 基线 + 1
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                int g = 0;
                while (e.Current != 0 && !e.GameOver && g++ < 10)
                    e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                if (e.GameOver) continue;
                Check(p0.MaxMP == BaseMp(p0.OwnTurnCount) + 1,
                    "有工人：行动点上限+1", "seed=" + seed + " MaxMP=" + p0.MaxMP + " 基线=" + BaseMp(p0.OwnTurnCount));
                Check(p0.MP == p0.MaxMP, "回合开始行动点回满（含工人加成）", "seed=" + seed);
                mpSeen++;

                // 两张工人也不叠加：仍只+1
                var w2 = new Card(96001 + seed * 10, CardType.Worker);
                p0.Hand.Add(w2);
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                g = 0;
                while (e.Current != 0 && !e.GameOver && g++ < 10)
                    e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                if (e.GameOver) continue;
                Check(p0.MaxMP == BaseMp(p0.OwnTurnCount) + 1,
                    "两张工人：仍只+1（不叠加）", "seed=" + seed + " MaxMP=" + p0.MaxMP);
                stackSeen++;

                // 工人离手 → 加成消失（顺手再清一遍手牌里抽到的随机工人，防回合开始抽牌干扰）
                p0.Hand.Remove(w1); p0.Hand.Remove(w2);
                e.SideDeck.Add(w1); e.SideDeck.Add(w2);
                foreach (var c in p0.Hand.Where(c => c.Type == CardType.Worker).ToList())
                { p0.Hand.Remove(c); e.SideDeck.Add(c); }
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                g = 0;
                while (e.Current != 0 && !e.GameOver && g++ < 10)
                    e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                if (e.GameOver) continue;
                Check(p0.MaxMP == BaseMp(p0.OwnTurnCount),
                    "工人离手：行动点加成消失", "seed=" + seed + " MaxMP=" + p0.MaxMP);
            }
            Check(mpSeen >= 8, "行动点+1分支覆盖", "seen=" + mpSeen);
            Check(stackSeen >= 8, "不叠加分支覆盖", "seen=" + stackSeen);

            // ---- 合成奖励方案0=奖励抽1张牌（2026-10-14 起默认改为方案C护盾，抽牌方案需显式设置后回归） ----
            for (int seed = 1; seed <= 10; seed++)
            {
                var e = NewGen2(seed);
                e.CraftRewardStyle[0] = 0;   // 显式回退为抽牌方案（默认已是方案C护盾）
                var p0 = e.Players[0];
                foreach (var c in p0.Hand.Where(c => c.Type != CardType.Hero).ToList())
                { p0.Hand.Remove(c); e.SideDeck.Add(c); }
                p0.Hand.Add(new Card(96100 + seed, CardType.Warden));
                p0.Hand.Add(new Card(96101 + seed, CardType.Police));
                p0.Hand.Add(new Card(96102 + seed, CardType.Minister));
                int handBefore = p0.Hand.Count;
                int totalBefore = e.TotalCards;
                int sideBefore = e.SideDeck.Count;

                e.ApplyAction(new GameAction { Kind = ActionKind.Craft, TargetType = CardType.Jailer });

                Check(p0.Hand.Any(c => c.Type == CardType.Jailer && c.SynthHP == GameEngine2.SYNTH_HP),
                    "合成产出合成牌（2点生命）", "seed=" + seed);
                Check(p0.Hand.Count == handBefore - 1, "合成后手牌-1（-3配方+1合成牌+1抽牌）", "seed=" + seed);
                Check(e.History.Any(m => m.Contains("奖励抽了1张牌")), "合成奖励=抽1张牌（2026-10-13 固定）", "seed=" + seed);
                Check(e.SideDeck.Count == sideBefore - 1, "奖励从牌堆抽1张", "seed=" + seed);
                Check(e.TotalCards == totalBefore, "合成后卡牌守恒", "seed=" + seed);
            }

            // ---- 工人护盾废止：HP=0 手里有工人也出局 ----
            for (int seed = 1; seed <= 10; seed++)
            {
                var e2 = NewGen2(seed);
                var v = e2.Players[1];
                foreach (var c in v.Hand.Where(c => c.Type != CardType.Hero).ToList())
                { v.Hand.Remove(c); e2.Discard.Add(c); }
                v.Hand.Add(new Card(96200 + seed, CardType.Worker));   // 手里明牌一张工人
                v.Hand.Add(new Card(96201 + seed, CardType.Worker));
                v.HP = 1;
                // 用军机大臣勒索英雄牌做确定性扣血
                var attacker = e2.Players[0];
                foreach (var c in attacker.Hand.Where(c => c.Type != CardType.Hero).ToList())
                { attacker.Hand.Remove(c); e2.Discard.Add(c); }
                attacker.Hand.Add(new Card(96202 + seed, CardType.GrandMinister));

                e2.ApplyAction(new GameAction { Kind = ActionKind.Extort, TargetType = CardType.Hero, SourceCardId = 96202 + seed });
                Check(v.HP == 0, "勒索英雄牌：HP降到0", "seed=" + seed);
                Check(!v.Alive, "护盾废止：HP=0 立即出局（工人救不了）", "seed=" + seed);
                Check(e2.History.Any(m => m.Contains("出局")), "出局已播报", "seed=" + seed);
                Check(e2.EliminationOrder.Contains(1), "出局顺序已记录", "seed=" + seed);
            }
        }

        // ------------------------------------------------------------------
        // 12. 合成奖励·方案C护盾（2026-10-13 用户设计）：持续 X 回合，吸收 1 点伤害。
        //    吸收范围=玩家本体：任何攻击实例（普攻/毒药/军火库/强攻追加/炸弹随机攻击——
        //    在抽牌前整个吞掉，同时保护本体HP与整个手牌）以及勒索【英雄】的1点直伤。
        //    到期/吸满即消散。CraftRewardStyle[seat]==2 时替代抽牌奖励。
        // ------------------------------------------------------------------
        static void ShieldTests()
        {
            Section("合成奖励·方案C护盾（持续" + GameEngine2.CRAFT_SHIELD_TURNS + "回合，吸收" + GameEngine2.CRAFT_SHIELD_CHARGES + "点伤害）");
            for (int seed = 1; seed <= 10; seed++)
            {
                // ---- A. 合成发护盾：不抽牌，充能/到期正确 ----
                var e = NewGen2(seed);
                var p0 = e.Players[0];
                foreach (var c in p0.Hand.Where(c => c.Type != CardType.Hero).ToList())
                { p0.Hand.Remove(c); e.SideDeck.Add(c); }
                p0.Hand.Add(new Card(96300 + seed, CardType.Warden));
                p0.Hand.Add(new Card(96301 + seed, CardType.Police));
                p0.Hand.Add(new Card(96302 + seed, CardType.Minister));
                e.CraftRewardStyle[0] = 2;
                int handBefore = p0.Hand.Count;
                int sideBefore = e.SideDeck.Count;

                e.ApplyAction(new GameAction { Kind = ActionKind.Craft, TargetType = CardType.Jailer });

                Check(p0.ShieldCharges == GameEngine2.CRAFT_SHIELD_CHARGES, "合成发放护盾（充能正确）", "seed=" + seed);
                Check(p0.ShieldExpireOwnTurn == p0.OwnTurnCount + GameEngine2.CRAFT_SHIELD_TURNS,
                    "护盾到期回合正确", "seed=" + seed);
                Check(p0.Hand.Count == handBefore - 2, "护盾方案：手牌-2（-3配方+1合成牌，不抽牌）", "seed=" + seed);
                Check(e.SideDeck.Count == sideBefore, "护盾方案：不碰牌堆", "seed=" + seed);
                Check(e.History.Any(m => m.Contains("获得【护盾】")), "护盾发放已播报", "seed=" + seed);

                // ---- B. 攻击被护盾整个吞掉：不抽牌、不掉HP ----
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // →1号
                int p0Hand = p0.Hand.Count, p0Hp = p0.HP;
                e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 0 });
                Check(p0.ShieldCharges == 0, "攻击被吸收：护盾耗尽", "seed=" + seed);
                Check(p0.Hand.Count == p0Hand && p0.HP == p0Hp,
                    "攻击被吸收：手牌与HP无损（整个攻击被吞）", "seed=" + seed);
                Check(e.History.Any(m => m.Contains("【护盾】吸收了这次攻击")), "吸收已播报", "seed=" + seed);

                // 耗尽后不再保护：回到0号新回合，护盾仍是0
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // →2号
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // →0号新回合
                Check(p0.ShieldCharges == 0, "护盾耗尽不刷新", "seed=" + seed);

                // ---- C. 到期消散：安静熬过X个自己的回合 ----
                var e2 = NewGen2(seed);
                var q0 = e2.Players[0];
                foreach (var c in q0.Hand.Where(c => c.Type != CardType.Hero).ToList())
                { q0.Hand.Remove(c); e2.SideDeck.Add(c); }
                q0.Hand.Add(new Card(96400 + seed, CardType.Warden));
                q0.Hand.Add(new Card(96401 + seed, CardType.Police));
                q0.Hand.Add(new Card(96402 + seed, CardType.Minister));
                e2.CraftRewardStyle[0] = 2;
                e2.ApplyAction(new GameAction { Kind = ActionKind.Craft, TargetType = CardType.Jailer });
                int expire = q0.ShieldExpireOwnTurn;
                for (int i = 0; i < 3 * (GameEngine2.CRAFT_SHIELD_TURNS + 1) && q0.ShieldCharges > 0; i++)
                    e2.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                Check(q0.OwnTurnCount == expire + 1, "已走到到期回合之后", "seed=" + seed
                    + " own=" + q0.OwnTurnCount + " expire=" + expire);
                Check(q0.ShieldCharges == 0, "护盾到期消散", "seed=" + seed);
                Check(e2.History.Any(m => m.Contains("【护盾】到期消散")), "到期已播报", "seed=" + seed);

                // ---- D. 勒索【英雄】的1点直伤也被吸收 ----
                var e3 = NewGen2(seed);
                var v = e3.Players[1];
                v.ShieldCharges = 1;
                v.ShieldExpireOwnTurn = v.OwnTurnCount + GameEngine2.CRAFT_SHIELD_TURNS;   // 直接发盾
                int vHp = v.HP;
                var gm = e3.Players[0];
                foreach (var c in gm.Hand.Where(c => c.Type != CardType.Hero).ToList())
                { gm.Hand.Remove(c); e3.Discard.Add(c); }
                gm.Hand.Add(new Card(96500 + seed, CardType.GrandMinister));
                e3.ApplyAction(new GameAction { Kind = ActionKind.Extort, TargetType = CardType.Hero, SourceCardId = 96500 + seed });
                Check(v.HP == vHp, "勒索英雄：护盾吸收，HP无损", "seed=" + seed);
                Check(v.ShieldCharges == 0, "勒索英雄：护盾耗尽", "seed=" + seed);
            }
        }

        // ------------------------------------------------------------------
        // 13. 新英雄（2026-10-14）：美人计(Id 7) / 凯旋(Id 8) / 冷酷追猎(Id 9，2026-10-19 由骑士精神重做) / 致命一击(Id 10)
        //     （用户口中的编号8~11 = HeroRoster Id 7~10：现有池只有 Id 0~6）
        // ------------------------------------------------------------------
        static void Hero48Tests()
        {
            Section("新英雄：美人计 / 凯旋 / 冷酷追猎 / 致命一击（2026-10-14 入池，Id9 于 2026-10-19 重做）");

            // ---- 美人计（2026-10-19 最终版=被动，用户："复杂的规则是不好的"）：大臣结盟交出侍女→对方被魅惑——
            //      到持有者下个回合开始，持有者受到的攻击转移到魅惑目标身上走完整伤害判定（抽牌/掉HP/护盾/免疫/挡刀照常结算）----
            for (int seed = 1; seed <= 5; seed++)
            {
                var e = NewGen2Forced(seed, 7, 0, 0);
                var p1 = e.Players[1];
                foreach (var p in e.Players)
                    foreach (var c in p.Hand.Where(c => c.Type != CardType.Hero).ToList())
                    { p.Hand.Remove(c); e.SideDeck.Add(c); }
                int mId = 97000 + seed * 10;
                e.Players[0].Hand.Add(new Card(mId, CardType.Minister));
                e.Players[0].Hand.Add(new Card(mId + 1, CardType.Maid));
                p1.Hand.Add(new Card(mId + 2, CardType.Guard));
                e.Players[2].Hand.Add(new Card(mId + 3, CardType.Guard));

                Check(!e.GetLegalActions(0).Any(a => a.Kind == ActionKind.HeroPower), "美人计：被动英雄无主动技行动", "seed=" + seed);
                e.ApplyAction(new GameAction { Kind = ActionKind.Bribe, TargetSeat = 1, SourceCardId = mId, SecondCardId = mId + 1 });
                Check(p1.Hand.Any(c => c.Id == mId + 1), "美人计：侍女交到魅惑目标手中", "seed=" + seed);
                Check(e.History.Any(m => m.Contains("侍女牵线")), "美人计：侍女牵线=魅惑已生效", "seed=" + seed);
                Check(e.IsProtected(1, 0), "美人计：大臣结盟的1回合禁攻照常生效", "seed=" + seed);

                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 0→1
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 1→2
                int p1Before = p1.Hand.Count;
                int p0Before = e.Players[0].Hand.Count;
                e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 0 });
                Check(e.History.Any(m => m.Contains("魅惑生效")), "美人计：攻击被魅惑重定向", "seed=" + seed);
                Check(p1.Hand.Count == p1Before - 1 || p1.HP == 2, "美人计：魅惑目标代替承受（完整伤害判定：替掉一张牌或命中其英雄）", "seed=" + seed);
                Check(e.Players[0].Hand.Count == p0Before && e.Players[0].HP == 3, "美人计：持有者分毫未损", "seed=" + seed);

                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 2→0（魅惑到期）
                Check(!e.IsProtected(1, 0), "美人计：到自己的下回合开始即到期（禁攻与魅惑同时失效）", "seed=" + seed);
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 0→1
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 1→2
                int redir = e.History.Count(m => m.Contains("魅惑生效"));
                e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 0 });
                Check(e.History.Count(m => m.Contains("魅惑生效")) == redir, "美人计：到期后重定向不再触发", "seed=" + seed);
            }

            // 反例：大臣结盟交出守卫（非侍女）→ 无魅惑
            for (int seed = 1; seed <= 5; seed++)
            {
                var e2 = NewGen2Forced(seed, 7, 0, 0);
                var q1 = e2.Players[1];
                foreach (var p in e2.Players)
                    foreach (var c in p.Hand.Where(c => c.Type != CardType.Hero).ToList())
                    { p.Hand.Remove(c); e2.SideDeck.Add(c); }
                int gId = 97100 + seed * 10;
                e2.Players[0].Hand.Add(new Card(gId, CardType.Minister));
                e2.Players[0].Hand.Add(new Card(gId + 1, CardType.Guard));
                q1.Hand.Add(new Card(gId + 2, CardType.Guard));
                e2.ApplyAction(new GameAction { Kind = ActionKind.Bribe, TargetSeat = 1, SourceCardId = gId, SecondCardId = gId + 1 });
                Check(!e2.History.Any(m => m.Contains("侍女牵线")), "美人计反例：交出守卫无魅惑", "seed=" + seed);
                e2.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 0→1
                e2.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 1→2
                e2.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 0 });
                Check(!e2.History.Any(m => m.Contains("魅惑生效")), "美人计反例：无魅惑不重定向", "seed=" + seed);
            }

            // ---- 凯旋：淘汰玩家 → 2回合护盾 + 从遗物挑1张（AI自动，不含英雄牌）----
            for (int seed = 1; seed <= 5; seed++)
            {
                var e = NewGen2Forced(seed, 8, 0, 0);
                var p1 = e.Players[1];
                foreach (var p in e.Players)
                    foreach (var c in p.Hand.Where(c => c.Type != CardType.Hero).ToList())
                    { p.Hand.Remove(c); e.SideDeck.Add(c); }
                int gmId = 97200 + seed;
                e.Players[0].Hand.Add(new Card(gmId, CardType.GrandMinister));
                p1.Hand.Add(new Card(gmId + 1, CardType.Guard));   // 将成为遗物
                p1.HP = 1;

                e.ApplyAction(new GameAction { Kind = ActionKind.Extort, TargetType = CardType.Hero, SourceCardId = gmId });
                Check(!p1.Alive, "凯旋：目标HP归零出局", "seed=" + seed);
                var k = e.Players[0];
                Check(k.ShieldCharges == 1 && k.ShieldExpireOwnTurn == k.OwnTurnCount + GameEngine2.TRIUMPH_SHIELD_TURNS,
                    "凯旋：护盾发放（1点，持续2回合）", "seed=" + seed);
                Check(k.Hand.Any(c => c.Id == gmId + 1), "凯旋：AI从遗物中挑选1张牌", "seed=" + seed);
                Check(!k.Hand.Any(c => c.Type == CardType.Hero && c.Id == 9001), "凯旋：遗物不含英雄牌", "seed=" + seed);
                Check(p1.Hand.Count == 0, "凯旋：淘汰者手牌清空", "seed=" + seed);
            }

            // ---- 冷酷追猎（2026-10-19 重做，原骑士精神）：本回合蓝耗尽（<1）且未普攻 → 回合结束随机处决对手被封印的牌 ----
            for (int seed = 1; seed <= 5; seed++)
            {
                // 场景1（核心收益点）：别人的封印也能吃——seat2 的警察封 seat1，seat0（冷酷追猎）蓝耗尽收工 → 处决
                var e = NewGen2Forced(seed, 9, 0, 0);
                var p0 = e.Players[0]; var p1 = e.Players[1]; var p2 = e.Players[2];
                foreach (var p in e.Players)
                    foreach (var c in p.Hand.Where(c => c.Type != CardType.Hero).ToList())
                    { p.Hand.Remove(c); e.SideDeck.Add(c); }
                int guardId = 97300 + seed * 10, police2Id = guardId + 1;
                p1.Hand.Add(new Card(guardId, CardType.Guard));
                p2.Hand.Add(new Card(police2Id, CardType.Police));
                Check(!e.GetLegalActions(0).Any(a => a.Kind == ActionKind.HeroPower), "冷酷追猎：被动英雄无主动技行动", "seed=" + seed);
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 0→1
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 1→2
                e.ApplyAction(new GameAction { Kind = ActionKind.Seize, TargetSeat = 1, SourceCardId = police2Id });   // 2号位警察封1号位
                Check(e.History.Any(m => m.Contains("封住")), "冷酷追猎前置：封印已生效", "seed=" + seed);
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 2→0
                p0.MP = 0;                                                     // 行动点耗尽且本回合未普攻
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 0→1（触发处决）
                Check(e.History.Any(m => m.Contains("处决了")), "冷酷追猎：蓝耗尽未普攻→处决播报", "seed=" + seed);
                Check(e.Discard.Any(c => c.Id == guardId), "冷酷追猎：被封印的牌进弃牌堆", "seed=" + seed);
                Check(!p1.Hand.Any(c => c.Id == guardId), "冷酷追猎：从受害者手牌中移除", "seed=" + seed);

                // 场景2：还有蓝（MP≥1）→ 不触发
                var e2 = NewGen2Forced(seed + 100, 9, 0, 0);
                var q1 = e2.Players[1]; var q2 = e2.Players[2];
                foreach (var p in e2.Players)
                    foreach (var c in p.Hand.Where(c => c.Type != CardType.Hero).ToList())
                    { p.Hand.Remove(c); e2.SideDeck.Add(c); }
                int g2 = 97350 + seed * 10, pol2 = g2 + 1;
                q1.Hand.Add(new Card(g2, CardType.Guard));
                q2.Hand.Add(new Card(pol2, CardType.Police));
                e2.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                e2.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                e2.ApplyAction(new GameAction { Kind = ActionKind.Seize, TargetSeat = 1, SourceCardId = pol2 });
                e2.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 2→0
                // MP=3 满蓝收工
                e2.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                Check(!e2.History.Any(m => m.Contains("处决了")), "冷酷追猎：蓝未耗尽不触发", "seed=" + seed);
                Check(q1.Hand.Any(c => c.Id == g2), "冷酷追猎：不触发时被封印的牌留在手牌", "seed=" + seed);

                // 场景3：本回合普攻过 → 不触发
                var e3 = NewGen2Forced(seed + 200, 9, 0, 0);
                var r0 = e3.Players[0]; var r1 = e3.Players[1]; var r2 = e3.Players[2];
                foreach (var p in e3.Players)
                    foreach (var c in p.Hand.Where(c => c.Type != CardType.Hero).ToList())
                    { p.Hand.Remove(c); e3.SideDeck.Add(c); }
                int g3 = 97400 + seed * 10, pol3 = g3 + 1, meat3 = g3 + 2;
                r1.Hand.Add(new Card(g3, CardType.Guard));
                r1.Hand.Add(new Card(meat3, CardType.Witch));   // 普攻靶子（防侍卫干扰用非挡刀牌）
                r2.Hand.Add(new Card(pol3, CardType.Police));
                e3.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 0→1
                e3.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 1→2
                e3.ApplyAction(new GameAction { Kind = ActionKind.Seize, TargetSeat = 1, SourceCardId = pol3 });   // 2号位警察封1号位的Guard
                e3.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });   // 2→0
                r0.MP = 2;
                e3.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 1 });   // 普攻过（MP 2→1）
                r0.MP = 0;
                e3.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                Check(!e3.History.Any(m => m.Contains("处决了")), "冷酷追猎：本回合普攻过不触发", "seed=" + seed);

                // 场景4：无封印 → 不触发
                var e4 = NewGen2Forced(seed + 300, 9, 0, 0);
                foreach (var p in e4.Players)
                    foreach (var c in p.Hand.Where(c => c.Type != CardType.Hero).ToList())
                    { p.Hand.Remove(c); e4.SideDeck.Add(c); }
                e4.Players[0].MP = 0;
                e4.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                Check(!e4.History.Any(m => m.Contains("处决了")), "冷酷追猎：无封印不触发", "seed=" + seed);
            }

            // ---- 致命一击（2026-10-19 重做=被动，当晚加强手牌上限4→5）：攻击HP=1且手牌≤5的玩家必定命中其英雄（跳过抽牌/挡刀；护盾吸收仍生效）----
            for (int seed = 1; seed <= 5; seed++)
            {
                // 场景1：HP=1 且手牌2张（≤5）→ 必定命中英雄（斩杀）
                var e = NewGen2Forced(seed, 10, 0, 0);
                var p1 = e.Players[1];
                foreach (var p in e.Players)
                    foreach (var c in p.Hand.Where(c => c.Type != CardType.Hero).ToList())
                    { p.Hand.Remove(c); e.SideDeck.Add(c); }
                Check(!e.GetLegalActions(0).Any(a => a.Kind == ActionKind.HeroPower), "致命一击：被动英雄无主动技行动", "seed=" + seed);
                p1.Hand.Add(new Card(97400 + seed, CardType.Guard));
                p1.Hand.Add(new Card(97401 + seed, CardType.Witch));
                p1.HP = 1;
                e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 1 });
                Check(e.History.Any(m => m.Contains("杀招必定命中")), "致命一击：HP=1且手牌≤5 → 播报必定命中", "seed=" + seed);
                Check(p1.HP == 0 && !p1.Alive, "致命一击：命中英雄直接斩杀", "seed=" + seed);

                // 场景2：HP=1 且手牌5张（=上限，2026-10-19 晚加强后应触发）
                var e2 = NewGen2Forced(seed + 100, 10, 0, 0);
                var p2 = e2.Players[1];
                foreach (var p in e2.Players)
                    foreach (var c in p.Hand.Where(c => c.Type != CardType.Hero).ToList())
                    { p.Hand.Remove(c); e2.SideDeck.Add(c); }
                for (int k = 0; k < 5; k++) p2.Hand.Add(new Card(97410 + seed * 10 + k, CardType.Police));
                p2.HP = 1;   // 手牌=[5×Police, 英雄牌]，5张非英雄 ≤5 → 触发
                e2.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 1 });
                Check(e2.History.Any(m => m.Contains("杀招必定命中")), "致命一击：手牌5张（=上限）触发", "seed=" + seed);
                Check(p2.HP == 0 && !p2.Alive, "致命一击：手牌5张边界斩杀", "seed=" + seed);

                // 场景2b：HP=1 但手牌6张（>5）→ 不触发（仍走随机抽牌判定）
                var e2b = NewGen2Forced(seed + 150, 10, 0, 0);
                var p2b = e2b.Players[1];
                foreach (var p in e2b.Players)
                    foreach (var c in p.Hand.Where(c => c.Type != CardType.Hero).ToList())
                    { p.Hand.Remove(c); e2b.SideDeck.Add(c); }
                for (int k = 0; k < 6; k++) p2b.Hand.Add(new Card(97510 + seed * 10 + k, CardType.Police));
                p2b.HP = 1;
                e2b.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 1 });
                Check(!e2b.History.Any(m => m.Contains("杀招必定命中")), "致命一击：手牌6张（>5）不触发（按普通随机判定结算）", "seed=" + seed);

                // 场景3：HP=2 且手牌≤5 → 不触发
                var e3 = NewGen2Forced(seed + 200, 10, 0, 0);
                var p3 = e3.Players[1];
                foreach (var p in e3.Players)
                    foreach (var c in p.Hand.Where(c => c.Type != CardType.Hero).ToList())
                    { p.Hand.Remove(c); e3.SideDeck.Add(c); }
                p3.Hand.Add(new Card(97460 + seed, CardType.Guard));
                p3.HP = 2;   // 手牌=[Guard, 英雄牌]，HP=2
                e3.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 1 });
                Check(!e3.History.Any(m => m.Contains("杀招必定命中")), "致命一击：HP=2不触发", "seed=" + seed);

                // 场景4：目标有护盾 → 护盾先吸收（保护本体），必定命中也不穿透
                var e4 = NewGen2Forced(seed + 300, 10, 0, 0);
                var p4 = e4.Players[1];
                foreach (var p in e4.Players)
                    foreach (var c in p.Hand.Where(c => c.Type != CardType.Hero).ToList())
                    { p.Hand.Remove(c); e4.SideDeck.Add(c); }
                p4.Hand.Add(new Card(97480 + seed, CardType.Guard));
                p4.HP = 1;
                p4.ShieldCharges = 1;
                p4.ShieldExpireOwnTurn = 99;
                e4.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = 1 });
                Check(e4.History.Any(m => m.Contains("护盾") && m.Contains("吸收")), "致命一击：护盾仍先吸收（不穿透护盾）", "seed=" + seed);
                Check(p4.HP == 1 && p4.Alive, "致命一击：护盾吸收后目标存活", "seed=" + seed);
            }
        }

        static void RunToEnd(GameEngine e)
        {
            int cap = 20000;
            while (!e.GameOver && cap-- > 0)
            {
                int seat = e.Current;
                e.ApplyAction(e.Brains[seat].ChooseAction(e, seat, e.GetLegalActions(seat)));
            }
        }

        static void RunToEnd(GameEngine2 e)
        {
            int cap = 20000;
            while (!e.GameOver && cap-- > 0)
            {
                int seat = e.Current;
                e.ApplyAction(((AIBrain2)e.Brains[seat]).ChooseAction(e, seat, e.GetLegalActions(seat)));
            }
        }

        // ------------------------------------------------------------------
        // 14. 联机协议（2026-10-20 阶段①，局域网真人测试的地基）
        //    行动/卡牌/抉择编解码 + 公开快照（信息隐藏：对手手牌内容绝不下发）
        // ------------------------------------------------------------------

        static void NetProtoTests()
        {
            Section("联机协议：编解码与公开快照（信息隐藏）");

            // ---- 行动编解码：全 ActionKind 往返 ----
            foreach (ActionKind k in Enum.GetValues(typeof(ActionKind)))
            {
                var a = new GameAction { Kind = k, TargetSeat = 2, SourceCardId = 12, TargetType = CardType.Witch, SecondCardId = 5 };
                var b = ActionCodec.Decode(ActionCodec.Encode(a));
                Check(b.Kind == a.Kind && b.TargetSeat == 2 && b.SourceCardId == 12
                    && b.TargetType == CardType.Witch && b.SecondCardId == 5, "行动编解码往返", k.ToString());
            }
            var bare = ActionCodec.Decode(ActionCodec.Encode(new GameAction { Kind = ActionKind.EndTurn }));
            Check(bare.Kind == ActionKind.EndTurn && bare.TargetSeat == -1 && bare.SourceCardId == -1,
                "行动缺省字段往返为默认");
            bool threw = false;
            try { ActionCodec.Decode("K=NotAKind"); } catch (FormatException) { threw = true; }
            Check(threw, "脏行动串抛 FormatException");

            // ---- 卡牌编解码：全字段往返 ----
            var src = new Card(7, CardType.Terrorist)
            {
                UsedSkillThisTurn = true, UsedSkillTurn = 4, PoisonUsed = true, AntidoteUsed = true,
                ExtortUsed = true, DiedStamp = 9, GuardProtectTurn = 3, LastGuardedTurn = 2,
                AppleTimer = 1, SynthHP = 2, ReviveUsed = true, PrayerUsed = true, PrayerDueTurn = 6,
                BombActive = true, BombPlanter = 1, BombDueTurn = 5, SeizeStamp = 8, SealedBy = 0
            };
            Check(CardCodec.Same(src, CardCodec.Decode(CardCodec.Encode(src))), "卡牌全字段编解码往返");
            var freshc = new Card(1, CardType.Maid);
            Check(CardCodec.Same(freshc, CardCodec.Decode(CardCodec.Encode(freshc))), "卡牌默认字段往返");

            // ---- 抉择请求编解码：三种 ChoiceKind ----
            var pend = new PendingChoice
            {
                Kind = ChoiceKind.GuardIntercept, Seat = 1, AttackerSeat = 0,
                AttackKind = ActionKind.ArsenalAttack,
                DrawnCard = new Card(11, CardType.Witch),
                Interceptors = new List<Card> { new Card(12, CardType.Guard), new Card(13, CardType.Paladin) { SynthHP = 1 } }
            };
            var pend2 = PendingWire.From(pend).ToPending();
            Check(pend2.Kind == pend.Kind && pend2.Seat == 1 && pend2.AttackerSeat == 0
                && pend2.AttackKind == ActionKind.ArsenalAttack, "挡刀抉择请求往返");
            Check(CardCodec.Same(pend.DrawnCard, pend2.DrawnCard), "挡刀被抽中牌往返");
            Check(pend2.Interceptors.Count == 2 && CardCodec.Same(pend.Interceptors[1], pend2.Interceptors[1]),
                "挡刀候选牌列表往返");

            var pick = new PendingChoice
            {
                Kind = ChoiceKind.PickFromDiscard, Seat = 2, Reason = "女巫·解药",
                Options = new List<Card> { new Card(20, CardType.Priest), new Card(21, CardType.Undead) { DiedStamp = 3 } }
            };
            var pick2 = PendingWire.From(pick).ToPending();
            Check(pick2.Reason == "女巫·解药" && pick2.Options.Count == 2
                && CardCodec.Same(pick.Options[1], pick2.Options[1]), "弃牌堆选取请求往返");

            // ---- 抉择应答编解码 ----
            var g1 = ChoiceResponse.Decode("G:1:12");
            Check(g1.IsGuard && g1.UseGuard && g1.CardId == 12, "挡刀应答(用牌)往返");
            var g0 = ChoiceResponse.Decode("G:0");
            Check(g0.IsGuard && !g0.UseGuard, "挡刀应答(不挡)往返");
            Check(ChoiceResponse.Decode("P:20").CardId == 20, "选取应答往返");
            Check(ChoiceResponse.Decode("D:2").TargetSeat == 2, "亡语目标应答往返");
            bool threw2 = false;
            try { ChoiceResponse.Decode("X:1"); } catch (FormatException) { threw2 = true; }
            Check(threw2, "脏抉择应答抛 FormatException");

            // ---- 公开快照：调度阶段（座位0持3券，1/2 无券）----
            var brains0 = new List<IPlayerBrain2> { new AIBrain2(1, 0, 3), new AIBrain2(2, 1, 3), new AIBrain2(3, 2, 3) };
            var em = new GameEngine2(Deck2(), new[] { "A", "B", "C" }, brains0, 42, null, new[] { 3, 0, 0 });
            Check(em.InMulligan, "调度阶段进行中（快照前提）");
            var snapM = PublicSnapshot.Build(em, 0);
            Check(snapM.InMulligan && snapM.Players[0].HandEnc.Count == em.Players[0].Hand.Count
                && snapM.Players[1].HandEnc.Count == 0 && snapM.Players[2].HandEnc.Count == 0,
                "调度快照：自己手牌完整、对手只有张数");
            Check(snapM.Players[0].MulliganTickets == 3, "调度券数随快照下发");
            Check(snapM.Legal.Count > 0 && snapM.DecodedLegal().All(a => a.Kind == ActionKind.Mulligan
                || a.Kind == ActionKind.EndMulligan), "调度阶段合法行动编码下发");
            var snapM1 = PublicSnapshot.Build(em, 1);
            Check(snapM1.Legal.Count == 0 && snapM1.Players[0].HandEnc.Count == 0
                && snapM1.Pending == null, "非调度座位：无行动权、看不到别人手牌与待决");

            // ---- 公开快照：对局中途信息隐藏 + 一致性（跑 40 步覆盖若干回合）----
            var brains = new List<IPlayerBrain2> { new AIBrain2(11, 0, 3), new AIBrain2(12, 1, 3), new AIBrain2(13, 2, 3) };
            var e = new GameEngine2(Deck2(), new[] { "A", "B", "C" }, brains, 7, null, new[] { 0, 0, 0 });
            foreach (var b in brains) { var ai = (AIBrain2)b; e.OnHostile += ai.NoteHostile; e.OnIntel += ai.NoteIntel; }
            int step = 0;
            while (step++ < 40 && !e.GameOver)
            {
                int s = e.Current;
                e.ApplyAction(brains[s].ChooseAction(e, s, e.GetLegalActions(s)));
            }
            for (int v = 0; v < 3; v++)
            {
                var snap = PublicSnapshot.Build(e, v);
                Check(snap.Players.Count == 3, "快照玩家数", "viewer=" + v);
                Check(snap.Players[v].HandEnc.Count == e.Players[v].Hand.Count, "自己手牌完整下发", "viewer=" + v);
                for (int o = 0; o < 3; o++)
                    if (o != v)
                        Check(snap.Players[o].HandEnc.Count == 0 && snap.Players[o].HandCount == e.Players[o].Hand.Count,
                            "对手手牌只有张数（无内容泄漏）", "viewer=" + v + " opp=" + o);
                Check(snap.Pending == null, "全 AI 局无待决外泄", "viewer=" + v);
                Check(snap.DiscardCount == e.Discard.Count && snap.SideDeckCount == e.SideDeck.Count,
                    "弃牌堆/牌堆计数一致", "viewer=" + v);
                Check(snap.History.Count == e.History.Count, "日志份数一致", "viewer=" + v);
                Check(snap.Players[v].HP == e.Players[v].HP && snap.Players[v].HeroName == e.Players[v].HeroName
                    && snap.Players[v].MaxMP == e.Players[v].MaxMP, "公开属性一致", "viewer=" + v);
            }
            // 行动权闸口：只有当前座位拿到 Legal，且解码后与引擎口径一致
            var curSnap = PublicSnapshot.Build(e, e.Current);
            var idleSnap = PublicSnapshot.Build(e, (e.Current + 1) % 3);
            Check(!e.GameOver && curSnap.Legal.Count > 0, "当前座位拿到合法行动");
            Check(idleSnap.Legal.Count == 0, "非当前座位合法行动为空");
            var engineLegal = e.GetLegalActions(e.Current)
                .Select(a => a.Kind + ":" + a.TargetSeat + ":" + a.SourceCardId + ":" + a.SecondCardId)
                .OrderBy(x => x).ToList();
            var snapLegal = curSnap.DecodedLegal()
                .Select(a => a.Kind + ":" + a.TargetSeat + ":" + a.SourceCardId + ":" + a.SecondCardId)
                .OrderBy(x => x).ToList();
            Check(engineLiveEqual(engineLegal, snapLegal), "快照 Legal 与引擎合法行动逐项一致");
        }

        static bool engineLiveEqual(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
