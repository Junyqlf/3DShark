using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

namespace SandKill.Fuzz2
{
    /// <summary>
    /// Fuzz 2 代引擎：抓取"新回合开始时技能/资源本应刷新却被锁"的状态。
    ///
    /// 断言点（每次 Current 易主后检查新当前玩家）：
    ///   1. 手牌里每张牌的技能"已用"标记必须已过期（回合戳机制，回合开始自动失效）；
    ///   2. MP 必须回满；
    ///   3. 未被沉默时，每张主动技牌在 GetLegalActions 里必须有对应行动（费用足够的前提下）；
    ///   4. 被扣押的牌归还后，若持有者是新当前玩家，同样适用第 1 条。
    ///
    /// 对局双方：AI + 脚本真人（HumanBrain2 + 外部脚本替 UI 提交 Pending），覆盖 Pending 路径。
    /// </summary>
    public static class Program
    {
        static int _passed, _failed;
        static int _turnChanges;

        static void Check(bool cond, string name, string detail = "")
        {
            if (cond) { _passed++; }
            else { _failed++; Console.WriteLine("  ✗ " + name + (detail.Length > 0 ? " | " + detail : "")); }
        }

        public static int Main()
        {
            int badGames = 0;
            for (int seed = 1; seed <= 60; seed++)
            {
                int failsBefore = _failed;
                RunGame(seed);
                if (_failed > failsBefore)
                {
                    badGames++;
                    Console.WriteLine("  ↑ 种子 " + seed + " 出现问题");
                    if (badGames >= 5) { Console.WriteLine("（已达 5 个出错种子，停止扫描）"); break; }
                }
            }
            Console.WriteLine("\n回合易主 " + _turnChanges + " 次，检查通过 " + _passed + " 项 / 失败 " + _failed + " 项");
            return _failed == 0 ? 0 : 1;
        }

        static void RunGame(int seed)
        {
            var e = NewGame(seed);
            int prevCurrent = e.Current;
            int guard = 20000;

            while (!e.GameOver && guard-- > 0)
            {
                if (e.Pending != null)
                {
                    // 替 UI 提交真人抉择（AI 的抉择引擎内部同步完成）
                    var pc = e.Pending;
                    if (pc.Seat == 0)
                    {
                        if (pc.Kind == ChoiceKind.GuardIntercept) e.SubmitGuardChoice(true);
                        else if (pc.Kind == ChoiceKind.PickFromDiscard) e.SubmitDiscardPick(pc.Options[0].Id);
                        else if (pc.Kind == ChoiceKind.DeathrattleAttack)
                        {
                            int t = e.Players.First(p => p.Alive && p.Seat != 0 && p.Hand.Count > 0).Seat;
                            e.SubmitDeathrattleTarget(t);
                        }
                    }
                    else Check(false, "Pending 出现在 AI 座位", "seed=" + seed + " seat=" + pc.Seat);
                    // 关键：提交可能触发"当前玩家被亡语反杀→回合易主"，这条路径也要断言刷新
                    if (e.Current != prevCurrent)
                    {
                        _turnChanges++;
                        CheckNewTurn(e, seed);
                        prevCurrent = e.Current;
                    }
                    continue;
                }

                int seat = e.Current;
                var legal = e.GetLegalActions(seat);
                GameAction act;
                if (seat == 0)
                {
                    // 脚本真人：优先用技能（轮流试每种），其次攻击，否则结束回合
                    act = legal.FirstOrDefault(a => a.SourceCardId >= 0 && a.Kind != ActionKind.EndTurn)
                          ?? legal.FirstOrDefault(a => a.Kind == ActionKind.Attack)
                          ?? legal.First(a => a.Kind == ActionKind.EndTurn);
                }
                else
                {
                    act = ((AIBrain2)e.Brains[seat]).ChooseAction(e, seat, legal);
                }
                e.ApplyAction(act);

                if (e.Current != prevCurrent)
                {
                    _turnChanges++;
                    CheckNewTurn(e, seed);
                    prevCurrent = e.Current;
                }
            }
        }

        /// <summary>新回合开始后的刷新断言。</summary>
        static void CheckNewTurn(GameEngine2 e, int seed)
        {
            int s = e.Current;
            var p = e.Players[s];

            Check(p.MP == p.MaxMP, "MP 已回满", "seed=" + seed + " seat=" + s + " MP=" + p.MP + "/" + p.MaxMP);

            foreach (var c in p.Hand)
                Check(!e.UsedThisTurn(c), "回合开始手牌技能已重置",
                    "seed=" + seed + " seat=" + s + " card=" + c + " usedTurn=" + c.UsedSkillTurn);

            if (!e.IsSilenced(s) && !e.GameOver)
            {
                var legal = e.GetLegalActions(s);
                foreach (var c in p.Hand)
                {
                    var def = CardDefs.Of(c.Type);
                    if (!def.HasActiveSkill) continue;
                    if (e.CardSilenced(c))
                    {
                        // 被封印的牌：本回合技能必须不可用（合成配料不算使用技能，Craft 行动的 SourceCardId 是"被消耗"而非"使用者"，需排除）
                        Check(!legal.Any(a => a.Kind != ActionKind.Craft && a.SourceCardId == c.Id), "被封印的牌本回合不可用",
                            "seed=" + seed + " seat=" + s + " card=" + c);
                        continue;
                    }
                    // 逐牌复刻 GetLegalActions 的可用条件（与引擎逐条对齐，避免误报）
                    bool anyTarget = e.Players.Any(t => t.Alive && t.Seat != s);
                    bool expect;
                    switch (c.Type)
                    {
                        case CardType.Exorcist:
                        case CardType.Police:
                            expect = p.MP >= def.SkillCost && anyTarget;
                            break;
                        case CardType.Witch:
                            expect = ((!c.PoisonUsed || e.SkillsFree(s)) && p.MP >= GameEngine2.COST_POISON
                                      && e.Players.Any(t => t.Alive && t.Seat != s && !e.IsProtected(s, t.Seat)))
                                  || ((!c.AntidoteUsed || e.SkillsFree(s)) && p.MP >= GameEngine2.COST_ANTIDOTE
                                      && e.Discard.Any(d => d.Type.IsRole() && d.Type != CardType.Hero && d.DiedStamp >= p.LastTurnStamp));
                            break;
                        case CardType.Minister:
                            expect = p.MP >= def.SkillCost && p.LastBribeTurn != p.OwnTurnCount
                                  && p.Hand.Any(x => x.Type != CardType.Hero && x.Id != c.Id && x.GuardProtectTurn < 0)
                                  && e.Players.Any(t => t.Alive && t.Seat != s
                                        && !e.IsProtected(t.Seat, s) && !e.IsProtected(s, t.Seat));
                            break;
                        case CardType.GrandMinister:
                            expect = p.MP >= def.SkillCost && (!c.ExtortUsed || e.SkillsFree(s));   // 限定技：整局一次（save期间不消耗）
                            break;
                        case CardType.ArmedGuard:
                            expect = p.MP >= def.SkillCost && !e.HasArmGuard(s);
                            break;
                        case CardType.Arsenal:
                            expect = p.MP >= def.SkillCost
                                  && e.Players.Any(t => t.Alive && t.Seat != s && !e.IsProtected(s, t.Seat));
                            break;
                        case CardType.Warden:
                            expect = p.MP >= def.SkillCost
                                  && p.Hand.Any(h => h.Type != CardType.Hero && h.Id != c.Id
                                                     && h.LastGuardedTurn != p.OwnTurnCount - 1
                                                     && h.GuardProtectTurn < 0);
                            break;
                        case CardType.Jailer:
                            expect = p.MP >= def.SkillCost
                                  && (e.SkillsFree(s) || c.SeizeStamp < 0 || p.OwnTurnCount - c.SeizeStamp >= 2)
                                  && e.Players.Any(t => t.Alive && t.Seat != s
                                        && t.Hand.Any(x => x.Type != CardType.Hero && x.GuardProtectTurn < 0));
                            break;
                        default:
                            expect = p.MP >= def.SkillCost;
                            break;
                    }
                    bool has = legal.Any(a => a.Kind != ActionKind.Craft && a.SourceCardId == c.Id);
                    if (expect != has)
                        Check(false, "主动技可用性与规则预期不符",
                            "seed=" + seed + " seat=" + s + " card=" + c + " MP=" + p.MP + " expect=" + expect + " legal=" + has);
                    else _passed++;
                }
            }
        }

        static GameEngine2 NewGame(int seed)
        {
            var deck = new Dictionary<CardType, int>
            {
                { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
                { CardType.Police, 4 }, { CardType.Guard, 4 }, { CardType.Witch, 3 },
                { CardType.Arsenal, 1 }, { CardType.ArmedGuard, 1 }, { CardType.Minister, 4 },
                { CardType.GrandMinister, 1 }, { CardType.Warden, 3 }, { CardType.Maid, 4 }
            };
            var brains = new List<IPlayerBrain2>
            {
                new HumanBrain2(),
                new AIBrain2(seed + 2, 1, 3), new AIBrain2(seed + 3, 2, 3)
            };
            var e = new GameEngine2(deck, new[] { "你", "AI-1", "AI-2" }, brains, seed);
            foreach (var b in brains) { var ai = b as AIBrain2; if (ai != null) { e.OnHostile += ai.NoteHostile; e.OnIntel += ai.NoteIntel; } }
            e.SkipMulligan();   // 调度券机制（2026-10-16）后加的：可用性扫描直接查合法列表，先跳过开局调度
            return e;
        }
    }
}
