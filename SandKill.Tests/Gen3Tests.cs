using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

namespace SandKill.Gen3Tests
{
    /// <summary>
    /// 3代「法术迸发」测试：升级=技能点（替代护盾）、learn 流程（1技能点+1行动点+1回合）、
    /// 六法术（炼金/清晰/点燃/雷霆/彗星反制/屏障）、AI 自对弈冒烟。
    /// 全部真人座位+直接ApplyAction，避免AI随机干扰；需要跨回合处用 EndTurn 空过驱动。
    /// </summary>
    class Program
    {
        static int _passed, _failed;

        static void Check(bool cond, string name, string detail = "")
        {
            if (cond) _passed++;
            else { _failed++; Console.WriteLine("  ✗ " + name + (detail == "" ? "" : " | " + detail)); }
        }

        public static int Main()
        {
            LevelUpSkillPointTest();
            LearnFlowTest();
            AlchemyTest();
            ClarityTest();
            IgniteTest();
            ThunderTest();
            CometCounterTest();
            BarrierTest();
            AISpellTest();
            NoTicketCtorTest();
            AISmokeTest();
            Console.WriteLine("\n通过 " + _passed + " 项 / 失败 " + _failed + " 项");
            return _failed == 0 ? 0 : 1;
        }

        static GameEngine3 NewGame(int seed)
        {
            var deck = new Dictionary<CardType, int>
            {
                { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
                { CardType.Police, 4 }, { CardType.Guard, 4 }, { CardType.Witch, 3 },
                { CardType.Arsenal, 1 }, { CardType.ArmedGuard, 1 }, { CardType.Minister, 4 },
                { CardType.GrandMinister, 1 }, { CardType.Warden, 3 }, { CardType.Maid, 4 },
                // 3代法术牌（2026-10-21 裁定）：炼金1/清晰2/点燃3/雷霆3/彗星3/屏障3
                { CardType.Alchemy, 1 }, { CardType.Clarity, 2 }, { CardType.Ignite, 3 },
                { CardType.Thunder, 3 }, { CardType.Comet, 3 }, { CardType.Barrier, 3 }
            };
            var brains = new List<IPlayerBrain2> { new HumanBrain2(), new HumanBrain2(), new HumanBrain2() };
            var e = new GameEngine3(deck, new[] { "H0", "H1", "H2" }, brains, seed);
            e.SkipMulligan();
            return e;
        }

        /// <summary>把手牌换成指定牌（原牌进弃牌堆守恒），返回保留的英雄牌是否在手。</summary>
        static void ResetHand(GameEngine2 e, int seat, params Card[] cards)
        {
            var p = e.Players[seat];
            foreach (var c in p.Hand.ToList())
            {
                p.Hand.Remove(c);
                if (c.Type != CardType.Hero) e.Discard.Add(c);
            }
            p.Hand.AddRange(cards);
        }

        /// <summary>空过回合直到 seat 的 OwnTurnCount 达到 target（上限 guard 次）。</summary>
        static void DriveOwnTurn(GameEngine2 e, int seat, int target, int guard = 40)
        {
            int g = 0;
            while (e.Players[seat].OwnTurnCount < target && !e.GameOver && g++ < guard)
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
        }

        /// <summary>移除所有手牌中的彗星（进弃牌堆）——避免彗星反制窗口干扰非彗星测试。</summary>
        static void StripComets(GameEngine2 e)
        {
            foreach (var p in e.Players)
                foreach (var c in p.Hand.Where(c => c.Type == CardType.Comet).ToList())
                { p.Hand.Remove(c); e.Discard.Add(c); }
        }

        // ------------------------------------------------------------------
        // 升级奖励 = 1 技能点 + 回1点英雄HP（升2升3都给，2026-10-21 用户试行）
        // ------------------------------------------------------------------
        static void LevelUpSkillPointTest()
        {
            Console.WriteLine("== 3代升级：6exp升2级=技能点+回1HP、8exp升3级=技能点+回1HP");
            bool done = false;
            for (int seed = 1; seed <= 20 && !done; seed++)
            {
                var e = NewGame(seed);
                int s0 = e.Current;
                var atk = e.Players[s0];
                int victim = (s0 + 1) % 3;
                var v = e.Players[victim];
                if (e.GameOver) continue;
                ResetHand(e, victim, Enumerable.Range(0, 8).Select(i => new Card(6000 + i, CardType.Maid)).ToArray());
                // 预置 HP=2 观察升2级回血
                atk.HP = 2;
                int strikes = 0;
                while (atk.HeroLevel < 1 && strikes < 40)
                {
                    if (!v.Hand.Any(c => c.Type == CardType.Maid))
                        v.Hand.Add(new Card(6900 + strikes, CardType.Maid));
                    e.BasicAttackUsed = false;
                    atk.MP = 3;
                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = victim });
                    // 真人座位被攻击可能弹挡刀抉择（合成牌/侍卫），一律拒绝
                    int g2 = 0;
                    while (e.Pending != null && g2++ < 5) e.SubmitGuardChoice(false, e.Pending.DrawnCard.Id);
                    strikes++;
                }
                if (atk.HeroLevel != 1) continue;
                Check(atk.SkillPoints == 1, "升2级获得1技能点", "seed=" + seed);
                Check(atk.HP == 3, "升2级回复1点英雄HP", "seed=" + seed + " HP=" + atk.HP);
                Check(atk.ShieldCharges == 0, "3代升级不再给护盾", "seed=" + seed);
                // 继续砍到升3级（累计8消灭）：奖励=再1技能点+再回1点英雄HP。重置 HP=2 观察回血
                atk.HP = 2;
                while (atk.HeroLevel < 2 && strikes < 60)
                {
                    if (!v.Hand.Any(c => c.Type == CardType.Maid))
                        v.Hand.Add(new Card(6900 + strikes, CardType.Maid));
                    e.BasicAttackUsed = false;
                    atk.MP = 3;
                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = victim });
                    int g3 = 0;
                    while (e.Pending != null && g3++ < 5) e.SubmitGuardChoice(false, e.Pending.DrawnCard.Id);
                    strikes++;
                }
                if (atk.HeroLevel == 2)
                {
                    Check(atk.EXP == GameEngine2.XP_LEVEL_3, "升3级经验=8", "seed=" + seed + " EXP=" + atk.EXP);
                    Check(atk.SkillPoints == 2, "升3级再获1技能点", "seed=" + seed);
                    Check(atk.HP == 3, "升3级回复1点英雄HP", "seed=" + seed + " HP=" + atk.HP);
                    // 升满后不再累积
                    int expCap = atk.EXP;
                    if (!v.Hand.Any(c => c.Type == CardType.Maid)) v.Hand.Add(new Card(6999, CardType.Maid));
                    e.BasicAttackUsed = false; atk.MP = 3;
                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = victim });
                    int g4 = 0;
                    while (e.Pending != null && g4++ < 5) e.SubmitGuardChoice(false, e.Pending.DrawnCard.Id);
                    Check(atk.EXP == expCap && atk.HeroLevel == 2, "升满3级后经验不再累积", "seed=" + seed);
                    done = true;
                }
            }
            Check(done, "升级场景已验证（含升3级奖励）");
        }

        // ------------------------------------------------------------------
        // learn：1技能点+1行动点，下回合才学会；学会前白板
        // ------------------------------------------------------------------
        static void LearnFlowTest()
        {
            Console.WriteLine("== learn：1技能点+1行动点+1回合；白板期不可释放");
            var e = NewGame(1);
            int s0 = e.Current;
            var me = e.Players[s0];
            var ignite = new Card(6101, CardType.Ignite);
            int victim = (s0 + 1) % 3;
            ResetHand(e, s0, ignite);
            ResetHand(e, victim, new Card(6102, CardType.Maid));
            me.SkillPoints = 1;
            me.MP = 3;

            // 未学会时释放 = 白板
            e.ApplyAction(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = 6101, TargetSeat = victim, SecondCardId = 6102 });
            Check(me.Hand.Any(c => c.Id == 6101), "白板期释放失败：法术牌留在手牌", "");
            Check(e.History.Any(m => m.Contains("纯白板")), "白板期释放有播报", "");

            // 没有技能点时不可 learn
            me.SkillPoints = 0;
            e.ApplyAction(new GameAction { Kind = ActionKind.Learn, TargetType = CardType.Ignite });
            Check(!e.HasSpell(s0, CardType.Ignite) && e.LearningSpell(s0) == 0, "无技能点不能学习", "");

            // learn：1技能点+1行动点
            me.SkillPoints = 1;
            e.ApplyAction(new GameAction { Kind = ActionKind.Learn, TargetType = CardType.Ignite });
            Check(me.SkillPoints == 0, "learn 消耗1技能点", "");
            Check(me.MP == 2, "learn 消耗1行动点", "");
            Check(e.LearningSpell(s0) == CardType.Ignite, "正在研习点燃", "");
            Check(!e.HasSpell(s0, CardType.Ignite), "本回合还没学会", "");

            // 下回合开始才学会
            DriveOwnTurn(e, s0, 2);
            Check(e.HasSpell(s0, CardType.Ignite), "下个自己的回合学会", "");
            Check(e.LearningSpell(s0) == 0, "研习状态清空", "");
        }

        // ------------------------------------------------------------------
        // 炼金术：法术牌洗入牌库并重抽等量
        // ------------------------------------------------------------------
        static void AlchemyTest()
        {
            Console.WriteLine("== 炼金术：法术牌洗回牌库，重抽等量");
            var e = NewGame(2);
            int s0 = e.Current;
            var me = e.Players[s0];
            var alch = new Card(6201, CardType.Alchemy);
            var thun = new Card(6202, CardType.Thunder);
            StripComets(e);
            ResetHand(e, s0, alch, thun, new Card(6203, CardType.Maid), new Card(6204, CardType.Guard));
            me.SkillPoints = 1;
            e.ApplyAction(new GameAction { Kind = ActionKind.Learn, TargetType = CardType.Alchemy });
            DriveOwnTurn(e, s0, 2);
            Check(e.HasSpell(s0, CardType.Alchemy), "已学会炼金术", "");

            int handBefore = me.Hand.Count;
            int sideBefore = e.SideDeck.Count;
            int totalBefore = e.TotalCards;
            e.ApplyAction(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = 6201, TargetType = CardType.Alchemy });
            Check(e.Discard.Any(c => c.Id == 6201), "炼金术释放后消耗进弃牌堆", "");
            // 手牌 = 原手牌 - 1（炼金术本身消耗）；其余法术牌洗回牌库并等量补入
            Check(me.Hand.Count == handBefore - 1, "重抽等量：净减1张（被释放的炼金术）", "");
            Check(e.TotalCards == totalBefore, "卡牌守恒（炼金不增减牌）", "");
            Check(!me.Hand.Any(c => c.Id == 6201), "炼金术牌不在手牌", "");
        }

        // ------------------------------------------------------------------
        // 清晰术（2026-10-21 重做）：1行动点看牌库顶3张选1张入手，其余原序放回
        // ------------------------------------------------------------------
        static void ClarityTest()
        {
            Console.WriteLine("== 清晰术：看库顶3张选1张入手");
            var e = NewGame(3);
            StripComets(e);
            int s0 = e.Current;
            var me = e.Players[s0];
            var clarity = new Card(6301, CardType.Clarity);
            ResetHand(e, s0, clarity);
            me.SkillPoints = 1;
            me.MP = 3;

            // 白板期不可释放
            e.ApplyAction(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = 6301 });
            Check(me.Hand.Any(c => c.Id == 6301), "未学会时清晰术留在手牌（白板）", "");

            // learn → 下回合学会
            e.ApplyAction(new GameAction { Kind = ActionKind.Learn, TargetType = CardType.Clarity });
            Check(me.SkillPoints == 0 && me.MP == 2, "learn 扣1技能点+1行动点", "");
            DriveOwnTurn(e, s0, me.OwnTurnCount + 1);
            Check(e.Current == s0, "回到自己回合", "");
            Check(e.HasSpell(s0, CardType.Clarity), "已学会清晰术", "");

            // 记录库顶3张（出牌前）
            var top3 = e.SideDeck.Take(Math.Min(3, e.SideDeck.Count)).ToList();
            Check(top3.Count == 3, "牌库顶有3张可选", "实际 " + top3.Count);
            int sideBefore = e.SideDeck.Count;
            me.MP = 3;
            var act = e.GetLegalActions(s0).FirstOrDefault(a => a.Kind == ActionKind.CastSpell && a.SourceCardId == 6301);
            Check(act != null, "学会后清晰术可释放", "");
            if (act == null) return;
            e.ApplyAction(act);
            Check(me.MP == 2, "清晰术消耗1行动点", "MP=" + me.MP);
            Check(e.Discard.Any(c => c.Id == 6301), "清晰术释放后消耗", "");
            Check(e.Pending != null && e.Pending.Kind == ChoiceKind.ClarityPick, "弹出清晰术选牌抉择", "");
            if (e.Pending == null) return;
            Check(e.Pending.Options.Select(c => c.Id).SequenceEqual(top3.Select(c => c.Id)), "抉择内容=库顶3张", "");

            // 选第2张（top3[1]）
            var pick = top3[1];
            e.SubmitClarityPick(pick.Id);
            Check(e.Pending == null, "提交后抉择清空", "");
            Check(me.Hand.Any(c => c.Id == pick.Id), "选中的牌加入手牌", "");
            Check(e.SideDeck.Count == sideBefore - 1, "牌库净减1张（选走的）", "");
            var back = e.SideDeck.Take(2).Select(c => c.Id).ToList();
            Check(back[0] == top3[0].Id && back[1] == top3[2].Id, "其余2张按原序放回库顶", "");
        }

        // ------------------------------------------------------------------
        // 点燃：2回合后阵亡 + 军火库同步炸毁
        // ------------------------------------------------------------------
        static void IgniteTest()
        {
            Console.WriteLine("== 点燃：2回合后阵亡；军火库同步炸毁");
            bool done = false, arsenalDone = false;
            for (int seed = 1; seed <= 20 && !(done && arsenalDone); seed++)
            {
                var e = NewGame(seed);
                int s0 = e.Current;
                var me = e.Players[s0];
                int victim = (s0 + 1) % 3;
                var v = e.Players[victim];
                var ign = new Card(6401, CardType.Ignite);
                var maid = new Card(6402, CardType.Maid);
                StripComets(e);
                ResetHand(e, s0, ign);
                ResetHand(e, victim, maid);
                me.SkillPoints = 1;
                e.ApplyAction(new GameAction { Kind = ActionKind.Learn, TargetType = CardType.Ignite });
                DriveOwnTurn(e, s0, 2);
                if (!e.HasSpell(s0, CardType.Ignite)) continue;
                int igniteTurn = me.OwnTurnCount;

                e.ApplyAction(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = 6401, TargetSeat = victim, SecondCardId = 6402 });
                Check(e.Discard.Any(c => c.Id == 6401), "点燃释放后消耗", "seed=" + seed);
                Check(v.Hand.Any(c => c.Id == 6402), "点燃施加时被标记牌还在手牌", "seed=" + seed);

                // 1 回合后未到点
                DriveOwnTurn(e, s0, igniteTurn + 1);
                Check(v.Hand.Any(c => c.Id == 6402), "1回合后未阵亡", "seed=" + seed);
                // 2 回合后到点
                DriveOwnTurn(e, s0, igniteTurn + 2);
                if (!done)
                {
                    Check(e.Discard.Any(c => c.Id == 6402), "2回合后被点燃的牌阵亡进弃牌堆", "seed=" + seed);
                    Check(me.EXP >= 1, "点燃击杀计入经验", "seed=" + seed);
                    done = true;
                }

                // 军火库同步炸毁（同局再点一张，受害者手牌放军火库）
                if (!arsenalDone && e.Players[victim].Alive)
                {
                    var ign2 = new Card(6411, CardType.Ignite);
                    var maid2 = new Card(6412, CardType.Maid);
                    var ars = new Card(6413, CardType.Arsenal);
                    me.Hand.Add(ign2);
                    v.Hand.Add(maid2);
                    v.Hand.Add(ars);
                    int t2 = me.OwnTurnCount;
                    e.ApplyAction(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = 6411, TargetSeat = victim, SecondCardId = 6412 });
                    DriveOwnTurn(e, s0, t2 + 2);
                    Check(e.Discard.Any(c => c.Id == 6412) && e.Discard.Any(c => c.Id == 6413),
                        "点燃到点：目标牌阵亡且军火库同步炸毁", "seed=" + seed);
                    arsenalDone = true;
                }
            }
            Check(done, "点燃到点场景已验证");
            Check(arsenalDone, "军火库同步炸毁场景已验证");
        }

        // ------------------------------------------------------------------
        // 雷霆：攻击次数 = 目标手牌数 × 25% 四舍五入
        // ------------------------------------------------------------------
        static void ThunderTest()
        {
            Console.WriteLine("== 雷霆：手牌数×25%四舍五入次攻击");
            var e = NewGame(4);
            int s0 = e.Current;
            var me = e.Players[s0];
            int victim = (s0 + 1) % 3;
            var v = e.Players[victim];
            var thun = new Card(6501, CardType.Thunder);
            StripComets(e);
            ResetHand(e, s0, thun);
            // 8 张手牌 → 8×25%=2 次攻击 → 死 2 张
            ResetHand(e, victim, Enumerable.Range(0, 8).Select(i => new Card(6510 + i, CardType.Maid)).ToArray());
            me.SkillPoints = 1;
            e.ApplyAction(new GameAction { Kind = ActionKind.Learn, TargetType = CardType.Thunder });
            DriveOwnTurn(e, s0, 2);
            Check(e.HasSpell(s0, CardType.Thunder), "已学会雷霆", "");

            int handBefore = v.Hand.Count;
            int logBefore = e.History.Count;
            me.MP = 3;
            e.ApplyAction(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = 6501, TargetSeat = victim, TargetType = CardType.Thunder });
            int killed = e.History.Skip(logBefore).Count(m => m.Contains("抽走了") || m.Contains("进入弃牌堆"));
            Check(v.Hand.Count == handBefore - 2, "8张手牌→2次攻击→阵亡2张", "剩 " + v.Hand.Count);
            Check(e.Discard.Any(c => c.Id == 6501), "雷霆释放后消耗", "");
            Check(me.MP == 2, "雷霆消耗1行动点", "");
        }

        // ------------------------------------------------------------------
        // 彗星反制：需先研习彗星（2026-10-21 用户裁定）→ 真人抉择 / AI威胁反制
        // ------------------------------------------------------------------
        static void CometCounterTest()
        {
            Console.WriteLine("== 彗星：需研习才能反制；抵消法术（真人抉择 + AI威胁反制）");
            var e = NewGame(5);
            int s0 = e.Current;
            var me = e.Players[s0];
            int victim = (s0 + 1) % 3;
            int bystander = (s0 + 2) % 3;
            var ign = new Card(6601, CardType.Ignite);
            var maid = new Card(6602, CardType.Maid);
            var comet = new Card(6603, CardType.Comet);
            StripComets(e);
            ResetHand(e, s0, ign);
            ResetHand(e, victim, maid);
            ResetHand(e, bystander, comet);
            me.SkillPoints = 1;
            e.ApplyAction(new GameAction { Kind = ActionKind.Learn, TargetType = CardType.Ignite });
            DriveOwnTurn(e, s0, 2);
            Check(e.HasSpell(s0, CardType.Ignite), "已学会点燃", "");

            // 未研习彗星：反制判定不触发，法术直接结算
            e.ApplyAction(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = 6601, TargetSeat = victim, SecondCardId = 6602 });
            Check(e.Pending == null, "持彗星但未研习：不弹反制抉择", "");
            Check(e.Players[bystander].Hand.Any(c => c.Id == 6603), "未研习：彗星不消耗", "");
            Check(e.IgniteRemaining(maid) >= 0 || e.History.Any(m => m.Contains("点燃") && m.Contains("缠上")),
                "未研习：点燃正常施加", "");

            // 轮到旁观者的回合：研习彗星（learn 只作用于当前回合玩家，必须先把回合驱动过去）
            DriveOwnTurn(e, bystander, e.Players[bystander].OwnTurnCount + 1);
            Check(e.Current == bystander, "轮到旁观者回合", "");
            var bs = e.Players[bystander];
            bs.SkillPoints = 1;
            bs.MP = 3;
            e.ApplyAction(new GameAction { Kind = ActionKind.Learn, TargetType = CardType.Comet });
            Check(e.LearningSpell(bystander) == CardType.Comet, "旁观者开始研习彗星", "");
            DriveOwnTurn(e, bystander, bs.OwnTurnCount + 1);
            Check(e.HasSpell(bystander, CardType.Comet), "旁观者已学会彗星", "");

            // 回到释放者回合：已研习 → 弹出 CometCounter 抉择
            DriveOwnTurn(e, s0, me.OwnTurnCount + 1);
            Check(e.Current == s0, "回到释放者回合", "");
            var ign2 = new Card(6611, CardType.Ignite);
            var maid2 = new Card(6612, CardType.Maid);
            me.Hand.Add(ign2);
            e.Players[victim].Hand.Add(maid2);
            e.ApplyAction(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = 6611, TargetSeat = victim, SecondCardId = 6612 });
            Check(e.Pending != null && e.Pending.Kind == ChoiceKind.CometCounter, "已研习：释放法术后弹出彗星反制抉择", "");
            if (e.Pending != null && e.Pending.Kind == ChoiceKind.CometCounter)
            {
                Check(e.Pending.Seat == bystander, "抉择属于研习者（旁观者）", "");
                e.SubmitCometChoice(true);
                Check(e.Discard.Any(c => c.Id == 6603), "消耗彗星抵消", "");
                Check(e.Discard.Any(c => c.Id == 6611), "被抵消的法术照样消耗", "");
                Check(e.Players[victim].Hand.Any(c => c.Id == 6612), "法术被抵消：目标牌未点燃", "");
                Check(e.Pending == null, "抉择清空", "");
            }

            // 放弃反制：法术正常结算
            var ign3 = new Card(6621, CardType.Ignite);
            var maid3 = new Card(6622, CardType.Maid);
            var comet3 = new Card(6623, CardType.Comet);
            me.Hand.Add(ign3);
            e.Players[victim].Hand.Add(maid3);
            e.Players[bystander].Hand.Add(comet3);
            e.ApplyAction(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = 6621, TargetSeat = victim, SecondCardId = 6622 });
            Check(e.Pending != null && e.Pending.Kind == ChoiceKind.CometCounter, "第三次释放再次弹出抉择", "");
            if (e.Pending != null && e.Pending.Kind == ChoiceKind.CometCounter)
            {
                e.SubmitCometChoice(false);
                Check(e.Pending == null, "放弃后无遗留抉择", "");
                Check(e.Players[bystander].Hand.Any(c => c.Id == 6623), "放弃则彗星不消耗", "");
                Check(e.IgniteRemaining(maid3) >= 0 || e.History.Any(m => m.Contains("点燃") && m.Contains("缠上")),
                    "放弃后法术正常结算（点燃施加）", "");
            }
        }

        // ------------------------------------------------------------------
        // 屏障（2026-10-21 新增）：1行动点获得1点持续2回合的护盾；法术牌消耗；可叠
        // ------------------------------------------------------------------
        static void BarrierTest()
        {
            Console.WriteLine("== 屏障：1蓝获得1点护盾（持续2回合）");
            var e = NewGame(7);
            StripComets(e);   // 施法前清掉真人座位手牌中的彗星，避免弹反制抉择
            int s0 = e.Current;
            var me = e.Players[s0];
            var barrier = new Card(6701, CardType.Barrier);
            ResetHand(e, s0, barrier);
            me.SkillPoints = 1;
            me.MP = 3;

            // 白板期不可释放
            e.ApplyAction(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = 6701 });
            Check(me.Hand.Any(c => c.Id == 6701), "未学会时屏障留在手牌（白板）", "");
            // learn → 下回合学会（学在回合开始时结算，DriveOwnTurn 停在 s0 的新回合）
            e.ApplyAction(new GameAction { Kind = ActionKind.Learn, TargetType = CardType.Barrier });
            Check(me.SkillPoints == 0 && me.MP == 2, "learn 扣1技能点+1行动点", "");
            DriveOwnTurn(e, s0, me.OwnTurnCount + 1);
            Check(e.Current == s0, "回到自己回合", "");
            Check(me.Hand.Any(c => c.Id == 6701), "屏障牌仍在手牌", "");
            var act = e.GetLegalActions(s0).FirstOrDefault(a => a.Kind == ActionKind.CastSpell && a.SourceCardId == 6701);
            Check(act != null, "学会后屏障可释放", "");
            if (act == null) return;   // 断言失败即止，避免空引用崩掉整套测试
            me.MP = 3;
            int shieldBefore = me.ShieldCharges;
            e.ApplyAction(act);
            Check(!me.Hand.Any(c => c.Id == 6701), "释放后屏障消耗（进弃牌堆）", "");
            Check(me.ShieldCharges == shieldBefore + 1, "获得1点护盾", "实际 " + me.ShieldCharges);
            Check(me.ShieldExpireOwnTurn == me.OwnTurnCount + 2, "护盾持续2个自己回合", "expire=" + me.ShieldExpireOwnTurn + " own=" + me.OwnTurnCount);
            Check(e.Discard.Any(c => c.Id == 6701), "屏障进弃牌堆", "");

            // 护盾可叠加：再放一张（learn 已学会的同种法术无需再学，直接可释放）
            var barrier2 = new Card(6702, CardType.Barrier);
            me.Hand.Add(barrier2);
            me.MP = 3;
            var act2 = e.GetLegalActions(s0).FirstOrDefault(a => a.Kind == ActionKind.CastSpell && a.SourceCardId == 6702);
            Check(act2 != null, "同种法术已学会：第二张直接可释放", "");
            if (act2 == null) return;
            int charges2 = me.ShieldCharges;
            me.MP = 3;
            e.ApplyAction(act2);
            Check(me.ShieldCharges == charges2 + 1, "护盾可叠加（+1）", "实际 " + me.ShieldCharges);
        }

        // ------------------------------------------------------------------
        // AI 研习+施法（2026-10-21）：learn 只在手里有该法术牌时入车；学会后按 EV 施法
        // ------------------------------------------------------------------
        static Dictionary<CardType, int> Gen3Deck()
        {
            return new Dictionary<CardType, int>
            {
                { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
                { CardType.Police, 4 }, { CardType.Guard, 4 }, { CardType.Witch, 3 },
                { CardType.Arsenal, 1 }, { CardType.ArmedGuard, 1 }, { CardType.Minister, 4 },
                { CardType.GrandMinister, 1 }, { CardType.Warden, 3 }, { CardType.Maid, 4 },
                { CardType.Alchemy, 1 }, { CardType.Clarity, 2 }, { CardType.Ignite, 3 },
                { CardType.Thunder, 3 }, { CardType.Comet, 3 }, { CardType.Barrier, 3 }
            };
        }

        static void AISpellTest()
        {
            Console.WriteLine("== AI研习/施法：手里有屏障+技能点时 learn，学会后施法");
            bool learnedOnce = false, castOnce = false;
            for (int seed = 1; seed <= 10 && !(learnedOnce && castOnce); seed++)
            {
                var brains = new List<IPlayerBrain2> { new AIBrain2(seed, 0, 3), new HumanBrain2(), new HumanBrain2() };
                var e = new GameEngine3(Gen3Deck(), new[] { "AI-0", "H1", "H2" }, brains, seed);
                e.SkipMulligan();
                StripComets(e);
                int s0 = e.Players.First(p => !p.IsHuman).Seat;
                var me = e.Players[s0];
                // 对手只剩英雄牌（随机攻击抽不到东西），排除普攻竞争；自己手里只有屏障
                foreach (var p in e.Players) if (p.Seat != s0) ResetHand(e, p.Seat);
                ResetHand(e, s0, new Card(6801, CardType.Barrier));
                me.SkillPoints = 1;
                me.MP = 3;

                var brain = (AIBrain2)e.Brains[s0];
                var act = brain.ChooseAction(e, s0, e.GetLegalActions(s0));
                if (act.Kind != ActionKind.Learn || act.TargetType != CardType.Barrier) continue;   // 别的行动竞争赢了就换种子
                learnedOnce = true;
                e.ApplyAction(act);
                DriveOwnTurn(e, s0, me.OwnTurnCount + 1);
                if (!e.HasSpell(s0, CardType.Barrier)) continue;
                me.MP = 3;
                var act2 = brain.ChooseAction(e, s0, e.GetLegalActions(s0));
                if (act2.Kind != ActionKind.CastSpell || act2.SourceCardId != 6801) continue;
                castOnce = true;
                int shieldBefore = me.ShieldCharges;
                e.ApplyAction(act2);
                Check(me.ShieldCharges == shieldBefore + 1, "AI施放屏障获得1点护盾", "seed=" + seed);
            }
            Check(learnedOnce, "AI在有技能点+屏障牌时选择研习");
            Check(castOnce, "AI学会后选择施放屏障");
        }

        // ------------------------------------------------------------------
        // 构造期崩溃回归：无人持调度券时基类构造会调 StartTurn(0)，曾致3代约8%的局开局NRE
        // ------------------------------------------------------------------
        static void NoTicketCtorTest()
        {
            Console.WriteLine("== 构造期：无调度券开局不崩（种子1~200）");
            int ok = 0;
            for (int seed = 1; seed <= 200; seed++)
            {
                try
                {
                    var brains = new List<IPlayerBrain2> { new HumanBrain2(), new HumanBrain2(), new HumanBrain2() };
                    var e = new GameEngine3(Gen3Deck(), new[] { "H0", "H1", "H2" }, brains, seed);
                    e.SkipMulligan();
                    ok++;
                }
                catch (Exception ex)
                {
                    Check(false, "构造不崩", "seed=" + seed + " " + ex.GetType().Name);
                    break;
                }
            }
            Check(ok == 200, "200个种子构造全部成功", "ok=" + ok);
        }

        // ------------------------------------------------------------------
        // AI 自对弈冒烟：新行动类型不崩、对局能打完
        // ------------------------------------------------------------------
        static void AISmokeTest()
        {
            Console.WriteLine("== AI自对弈冒烟（3代规则，种子1~5）");
            int gamesWithLearn = 0, gamesWithCast = 0;
            for (int seed = 1; seed <= 5; seed++)
            {
                var deck = new Dictionary<CardType, int>
                {
                    { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
                    { CardType.Police, 4 }, { CardType.Guard, 4 }, { CardType.Witch, 3 },
                    { CardType.Arsenal, 1 }, { CardType.ArmedGuard, 1 }, { CardType.Minister, 4 },
                    { CardType.GrandMinister, 1 }, { CardType.Warden, 3 }, { CardType.Maid, 4 },
                    { CardType.Alchemy, 1 }, { CardType.Clarity, 2 }, { CardType.Ignite, 3 },
                    { CardType.Thunder, 3 }, { CardType.Comet, 3 }, { CardType.Barrier, 3 }
                };
                var brains = new List<IPlayerBrain2>
                {
                    new AIBrain2(seed + 1, 0, 3), new AIBrain2(seed + 2, 1, 3), new AIBrain2(seed + 3, 2, 3)
                };
                var e = new GameEngine3(deck, new[] { "AI-0", "AI-1", "AI-2" }, brains, seed);
                foreach (var b in brains) { var ai = b as AIBrain2; if (ai != null) { e.OnHostile += ai.NoteHostile; e.OnIntel += ai.NoteIntel; } }
                int actions = 0;
                while (!e.GameOver && actions < 20000)
                {
                    int seat = e.Current;
                    if (e.Pending != null) { Check(false, "全AI对局不应产生真人抉择", "seed=" + seed); break; }
                    var legal = e.GetLegalActions(seat);
                    var act = ((AIBrain2)e.Brains[seat]).ChooseAction(e, seat, legal);
                    e.ApplyAction(act);
                    actions++;
                }
                Check(e.GameOver, "3代对局在步数上限内结束", "seed=" + seed + " 步数=" + actions);
                if (e.History.Any(m => m.Contains("开始研习"))) gamesWithLearn++;
                if (e.History.Any(m => m.Contains("释放【") && !m.Contains("还没学会"))) gamesWithCast++;
            }
            Check(gamesWithLearn > 0, "全AI对局中出现研习（" + gamesWithLearn + "/5 局）", "");
            Check(gamesWithCast > 0, "全AI对局中出现施法（" + gamesWithCast + "/5 局）", "");
        }
    }
}
