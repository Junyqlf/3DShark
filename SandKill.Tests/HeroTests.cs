using System;
using System.Collections.Generic;
using System.Linq;
using SandKill.Core;

namespace SandKill.HeroTests
{
    /// <summary>
    /// 英雄与合成系统正确性测试：攻城锤（前2回合额外普攻）、强攻（连击追加）、驱魔（女巫放回）、
    /// 警察封印、save（不进冷却）、解药复活窗口、牺牲（牧师挡刀）、合成牌（合成/复活/祈祷/挡刀/扣押/审判）。
    /// 驱动真实引擎、扫描多种子；构造部分直接改手牌以摆脱发牌随机性。
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
            BatteringRamTest();
            FocusFireTest();
            ExorciseReturnsWitchTest();
            PoliceSilenceTest();
            SaveNoCooldownTest();
            AntidoteFreshnessTest();
            PriestSacrificeTest();
            PriestSacStylesTest();
            SynthCraftTest();
            CraftWildcardTest();
            PriestessSkillsTest();
            SynthShieldTest();
            JailerHostageTest();
            InquisitorJudgeTest();
            LevelUpShieldTest();
            Console.WriteLine("\n通过 " + _passed + " 项 / 失败 " + _failed + " 项");
            return _failed == 0 ? 0 : 1;
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
                new AIBrain2(seed + 1, 0, 3), new AIBrain2(seed + 2, 1, 3), new AIBrain2(seed + 3, 2, 3)
            };
            var e = new GameEngine2(deck, new[] { "AI-0", "AI-1", "AI-2" }, brains, seed);
            foreach (var b in brains) { var ai = b as AIBrain2; if (ai != null) { e.OnHostile += ai.NoteHostile; e.OnIntel += ai.NoteIntel; } }
            e.SkipMulligan();   // 调度券机制（2026-10-16）后加的：场景测试直接ApplyAction，先跳过开局调度
            return e;
        }

        static int FindHeroSeat(GameEngine2 e, int heroId)
        {
            for (int i = 0; i < e.Players.Count; i++)
                if (e.Players[i].Alive && e.HeroOf(i).Id == heroId) return i;
            return -1;
        }

        static void DriveToSeat(GameEngine2 e, int seat)
        {
            int guard = 0;
            while (e.Current != seat && !e.GameOver && guard++ < 300)
            {
                int s = e.Current;
                e.ApplyAction(((AIBrain2)e.Brains[s]).ChooseAction(e, s, e.GetLegalActions(s)));
            }
        }

        // ------------------------------------------------------------------
        // 攻城锤：2026-10-20 起改为升级窗口——英雄升级（消灭8张牌）当回合与下个自己的回合
        // 各1次额外普攻机会（窗口=HeroLevel≥1 且 OwnTurnCount≤LevelTurn+1，见 GameEngine2.CanEarlyAttack）
        // ------------------------------------------------------------------
        static void BatteringRamTest()
        {
            Console.WriteLine("== 攻城锤：升级当回合与下个自己的回合各1次额外普攻");
            int inWindow = 0, afterWindow = 0, beforeLevel = 0, verifiedSeeds = 0;
            for (int seed = 1; seed <= 30 && verifiedSeeds < 3; seed++)
            {
                var e = NewGame(seed);
                int ram = FindHeroSeat(e, 3);
                if (ram < 0) continue;

                bool windowSeen = false, postSeen = false;
                for (int guard = 0; guard < 40 && !e.GameOver && !(windowSeen && postSeen); guard++)
                {
                    DriveToSeat(e, ram);
                    if (e.GameOver) break;
                    var me = e.Players[ram];
                    int t = me.OwnTurnCount;

                    var first = e.GetLegalActions(ram).Where(a => a.Kind == ActionKind.Attack).ToList();
                    if (first.Count == 0) { e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn }); continue; }
                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = first[0].TargetSeat });
                    var second = e.GetLegalActions(ram).Where(a => a.Kind == ActionKind.Attack).ToList();

                    // 以普攻结算后的状态判定：这次普攻本身可能就是第8杀当场升级，
                    // 升级当回合即进入窗口（CanEarlyAttack 锚定 LevelTurn=本回合）
                    int lt = e.Players[ram].LevelTurn;
                    bool leveled = e.Players[ram].HeroLevel >= 1;
                    bool expectWindow = leveled && t <= lt + 1;

                    if (!leveled)
                    {
                        Check(second.Count == 0, "攻城锤未升级时无额外普攻", "seed=" + seed + " turn=" + t);
                        beforeLevel++;
                    }
                    else if (expectWindow)
                    {
                        Check(second.Count > 0, "攻城锤升级窗口内（第" + t + "回合）普攻后有额外攻击机会",
                            "seed=" + seed + " LevelTurn=" + lt);
                        if (second.Count > 0)
                        {
                            e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = second[0].TargetSeat });
                            var third = e.GetLegalActions(ram).Where(a => a.Kind == ActionKind.Attack).ToList();
                            Check(third.Count == 0, "攻城锤额外机会每回合只给1次", "seed=" + seed + " turn=" + t);
                            inWindow++;
                            windowSeen = true;
                        }
                    }
                    else
                    {
                        Check(second.Count == 0, "攻城锤升级窗口结束后无额外普攻",
                            "seed=" + seed + " turn=" + t + " LevelTurn=" + lt);
                        afterWindow++;
                        postSeen = true;
                    }
                    e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                }
                if (windowSeen && postSeen) verifiedSeeds++;
            }
            Check(beforeLevel >= 1, "攻城锤升级前无额外普攻已验证", "实际 " + beforeLevel);
            Check(inWindow >= 2, "攻城锤升级窗口回合已验证（≥2个回合）", "实际 " + inWindow);
            Check(afterWindow >= 1, "攻城锤升级窗口外回合已验证", "实际 " + afterWindow);
        }

        // ------------------------------------------------------------------
        // 强攻：连续3次攻击同一目标（跨回合）→ 第3次后立即追加1次攻击
        // ------------------------------------------------------------------
        static void FocusFireTest()
        {
            Console.WriteLine("== 强攻：连击3次追加1次攻击（跨回合）");
            int triggered = 0, verifiedSeeds = 0;
            for (int seed = 1; seed <= 30 && verifiedSeeds < 4; seed++)
            {
                var e = NewGame(seed);
                int ff = FindHeroSeat(e, 4);
                if (ff < 0) continue;
                int victim = -1;
                for (int i = 0; i < 3; i++) if (i != ff && e.Players[i].Alive) { victim = i; break; }

                bool bonus = false;
                for (int turn = 1; turn <= 4 && !e.GameOver && !bonus; turn++)
                {
                    DriveToSeat(e, ff);
                    if (e.GameOver || e.Players[victim].Hand.Count == 0) break;
                    int handBefore = e.Players[victim].Hand.Count;
                    int hpBefore = e.Players[victim].HP;
                    int logBefore = e.History.Count;
                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = victim });
                    // 强攻追加攻击要么立刻发生（AI不弹pending），要么目标已死
                    bool fired = e.History.Skip(logBefore).Any(m => m.Contains("【强攻】触发"));
                    if (fired)
                    {
                        triggered++;
                        // 追加攻击在普攻结算后立即发生且不消耗任何资源（挡刀被吸收也算结算到目标——牺牲牧师/圣骑士挡刀+亡语回收会让手牌数不变；
                        // 亡灵免疫/侍女免疫军火库时牌回到手中，手牌与HP都不变——也算结算到了目标）
                        bool blocked = e.History.Skip(logBefore).Any(m => m.Contains("挺身而出"));
                        bool immune = e.History.Skip(logBefore).Any(m => m.Contains("免疫"));
                        bool absorbed = e.History.Skip(logBefore).Any(m => m.Contains("吸收了"));
                        bool deathrattle = e.History.Skip(logBefore).Any(m => m.Contains("亡语触发"));   // 牧师被消灭回收1张，手牌数不变
                        Check(e.Players[victim].Hand.Count < handBefore || e.Players[victim].HP < hpBefore || !e.Players[victim].Alive
                                || blocked || immune || absorbed || deathrattle,
                            "强攻追加攻击已结算到目标", "seed=" + seed);
                        // 换目标打1次：不应再触发追加（连击已重置）
                        int other = -1;
                        for (int i = 0; i < 3; i++) if (i != ff && i != victim && e.Players[i].Alive && e.Players[i].Hand.Count > 0) other = i;
                        if (other >= 0)
                        {
                            int logB2 = e.History.Count;
                            e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = other });
                            Check(!e.History.Skip(logB2).Any(m => m.Contains("【强攻】触发")),
                                "强攻换目标后连击重置，不连续触发", "seed=" + seed);
                        }
                    }
                    bonus = fired;
                    e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                }
                if (bonus) verifiedSeeds++;
            }
            Check(triggered >= 1, "强攻至少触发1次追加攻击", "实际 " + triggered + " 次");
        }

        // ------------------------------------------------------------------
        // 驱魔师：抽中女巫→放回（猎巫行动英雄已于2026-10-06移除，驱魔只处决亡灵）
        // ------------------------------------------------------------------
        static void ExorciseReturnsWitchTest()
        {
            Console.WriteLine("== 驱魔师：抽中女巫放回（猎巫行动已移除）");
            bool ctrlDone = false;
            for (int seed = 1; seed <= 30 && !ctrlDone; seed++)
            {
                var e2 = NewGame(seed + 500);
                int ctrl = e2.Current;
                DriveToSeat(e2, ctrl);
                if (e2.Current == ctrl && !e2.GameOver && !e2.IsSilenced(ctrl) && e2.Players[ctrl].MP >= GameEngine2.COST_EXORCISE)
                {
                    int victim = -1;
                    for (int i = 0; i < 3; i++) if (i != ctrl && e2.Players[i].Alive) { victim = i; break; }
                    var witch = new Card(9200, CardType.Witch);
                    e2.Players[victim].Hand.Clear();
                    e2.Players[victim].Hand.Add(witch);
                    var exo = new Card(9201, CardType.Exorcist);
                    e2.Players[ctrl].Hand.Add(exo);
                    int logBefore = e2.History.Count;
                    e2.ApplyAction(new GameAction { Kind = ActionKind.Exorcise, TargetSeat = victim, SourceCardId = 9201 });
                    Check(e2.History.Count > logBefore, "驱魔已结算", "seed=" + seed);
                    Check(e2.Players[victim].Hand.Count(x => x.Type == CardType.Witch) == 1,
                        "女巫不是异端：被放回", "seed=" + seed);
                    Check(e2.Players[victim].Hand.Count(x => x.Type == CardType.Witch && x.Id == 9200) == 1,
                        "放回的正是同一张女巫", "seed=" + seed);
                    ctrlDone = true;
                }
            }
            Check(ctrlDone, "驱魔放回女巫场景已验证");
        }
        // ------------------------------------------------------------------
        // 警察重做：沉默对手一张手牌（技能+被动封印到其回合结束），牌不离手
        // ------------------------------------------------------------------
        static void PoliceSilenceTest()
        {
            Console.WriteLine("== 警察：封印一张手牌（技能+被动），到期解除");
            bool skillDone = false, passiveDone = false, ctrlDone = false;
            for (int seed = 1; seed <= 30 && !(skillDone && passiveDone && ctrlDone); seed++)
            {
                // 技能封印：驱魔师被封 → 下个回合不可用 → 回合结束解除
                var e = NewGame(seed);
                int cop = e.Current;
                if (!skillDone && !e.IsSilenced(cop))
                {
                    int victim = -1;
                    for (int i = 0; i < 3; i++) if (i != cop && e.Players[i].Alive) { victim = i; break; }
                    var target = new Card(9301, CardType.Exorcist);
                    e.Players[victim].Hand.Clear();
                    e.Players[victim].Hand.Add(target);
                    e.Players[cop].Hand.Add(new Card(9302, CardType.Police));
                    e.ApplyAction(new GameAction { Kind = ActionKind.Seize, TargetSeat = victim, SourceCardId = 9302 });
                    Check(e.Players[victim].Hand.Contains(target), "封印的牌仍留在手牌（不离手）", "seed=" + seed);
                    Check(e.CardSilenced(target), "牌进入封印状态", "seed=" + seed);
                    e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                    DriveToSeat(e, victim);
                    if (!e.GameOver && e.Current == victim)
                    {
                        Check(!e.GetLegalActions(victim).Any(a => a.SourceCardId == 9301),
                            "被封印的牌本回合无技能行动", "seed=" + seed);
                        e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                        Check(!e.CardSilenced(target), "对方回合结束后封印解除", "seed=" + seed);
                        skillDone = true;
                    }
                }
                // 被动封印：亡灵被封 → 免疫攻击失效
                if (!passiveDone)
                {
                    var g = NewGame(seed);
                    int s0 = g.Current;
                    if (!g.IsSilenced(s0))
                    {
                        int victim = -1;
                        for (int i = 0; i < 3; i++) if (i != s0 && g.Players[i].Alive) { victim = i; break; }
                        var undead = new Card(9401, CardType.Undead);
                        g.Players[victim].Hand.Clear();
                        g.Players[victim].Hand.Add(undead);
                        g.Players[s0].Hand.Add(new Card(9402, CardType.Police));
                        g.ApplyAction(new GameAction { Kind = ActionKind.Seize, TargetSeat = victim, SourceCardId = 9402 });
                        if (g.CardSilenced(undead))
                        {
                            g.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = victim });
                            Check(g.Discard.Any(c => c.Id == 9401), "被封印的亡灵失去免疫，被抽走进弃牌堆", "seed=" + seed);
                            passiveDone = true;
                        }
                    }
                }
                // 对照：未封印的亡灵免疫攻击
                if (!ctrlDone)
                {
                    var g = NewGame(seed + 700);
                    int s0 = g.Current;
                    if (!g.IsSilenced(s0))
                    {
                        int victim = -1;
                        for (int i = 0; i < 3; i++) if (i != s0 && g.Players[i].Alive) { victim = i; break; }
                        var undead = new Card(9501, CardType.Undead);
                        g.Players[victim].Hand.Clear();
                        g.Players[victim].Hand.Add(undead);
                        g.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = victim });
                        Check(g.Players[victim].Hand.Any(c => c.Id == 9501), "对照组：未封印的亡灵免疫攻击回到手牌", "seed=" + seed);
                        ctrlDone = true;
                    }
                }
            }
            Check(skillDone, "技能封印场景已验证");
            Check(passiveDone, "被动封印场景已验证");
            Check(ctrlDone, "对照组场景已验证");
        }

        // ------------------------------------------------------------------
        // save真实语义（2026-09-30）：技能使用后不进冷却——
        // 回合技可连发；女巫双药/军机大臣限定技在save回合用掉也不消耗，之后照常可用
        // ------------------------------------------------------------------
        static void SaveNoCooldownTest()
        {
            Console.WriteLine("== save：技能使用后不进冷却（回合技连发 + 全局限定技不消耗）");
            bool freeDone = false, ctrlDone = false;
            for (int seed = 1; seed <= 40 && !(freeDone && ctrlDone); seed++)
            {
                var e = NewGame(seed);
                int sv = FindHeroSeat(e, 2);
                if (sv < 0) continue;
                DriveToSeat(e, sv);
                if (e.GameOver || e.Current != sv) continue;
                int victim = -1;
                for (int i = 0; i < 3; i++) if (i != sv && e.Players[i].Alive && e.Players[i].Hand.Count > 0) { victim = i; break; }
                if (victim < 0) continue;
                var witch = new Card(9601, CardType.Witch);
                e.Players[sv].Hand.Add(witch);
                var priest = new Card(9603, CardType.Priest);
                priest.DiedStamp = e.CurrentStamp;   // 视为本回合刚阵亡，满足"上一回合内"
                e.Discard.Add(priest);   // 解药的回收选项
                var gm = new Card(9602, CardType.GrandMinister);
                e.Players[sv].Hand.Add(gm);
                e.Players[sv].MP = 9;                             // 放开蓝量，单回合验证全部联动

                e.ApplyAction(new GameAction { Kind = ActionKind.HeroPower });
                Check(e.SkillsFree(sv), "save生效中", "seed=" + seed);
                if (!e.SkillsFree(sv)) continue;

                // 毒药：连发两次，且不置 PoisonUsed
                e.ApplyAction(new GameAction { Kind = ActionKind.PoisonAttack, TargetSeat = victim, SourceCardId = 9601 });
                Check(!witch.PoisonUsed, "save回合用毒药不消耗（PoisonUsed不置位）", "seed=" + seed);
                Check(e.GetLegalActions(sv).Any(a => a.Kind == ActionKind.PoisonAttack && a.SourceCardId == 9601),
                    "save回合毒药可立即再用", "seed=" + seed);
                e.ApplyAction(new GameAction { Kind = ActionKind.PoisonAttack, TargetSeat = victim, SourceCardId = 9601 });

                // 解药：不置 AntidoteUsed
                e.ApplyAction(new GameAction { Kind = ActionKind.Antidote, SourceCardId = 9601 });
                Check(!witch.AntidoteUsed, "save回合用解药不消耗", "seed=" + seed);

                // 军机大臣：勒索不置 ExtortUsed，可立即再来
                e.ApplyAction(new GameAction { Kind = ActionKind.Extort, SourceCardId = 9602, TargetType = CardType.Minister });
                Check(!gm.ExtortUsed, "save回合勒索不消耗（ExtortUsed不置位）", "seed=" + seed);
                Check(e.GetLegalActions(sv).Any(a => a.Kind == ActionKind.Extort && a.SourceCardId == 9602),
                    "save回合勒索可立即再用", "seed=" + seed);

                // 回合结束：save失效，但"不进冷却"的效果保留——技能没被消耗，下回合照常可用
                e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                Check(!e.SkillsFree(sv), "回合结束save失效", "seed=" + seed);
                DriveToSeat(e, sv);
                if (!e.GameOver && e.Current == sv)
                {
                    Check(!witch.PoisonUsed && !witch.AntidoteUsed && !gm.ExtortUsed,
                        "技能没有被消耗，跨回合仍可用（不进冷却）", "seed=" + seed);
                    // 轮转途中女巫牌可能被对方驱魔师等正常抽走——牌仍在手且存在合法目标时才要求毒药可用
                    bool witchInHand = e.Players[sv].Hand.Any(c => c.Id == 9601);
                    bool hasTarget = Enumerable.Range(0, 3).Any(i => i != sv && e.Players[i].Alive && e.Players[i].Hand.Count > 0);
                    if (witchInHand && hasTarget)
                        Check(e.GetLegalActions(sv).Any(a => a.Kind == ActionKind.PoisonAttack && a.SourceCardId == 9601),
                            "下回合毒药仍可用", "seed=" + seed);
                }
                freeDone = true;

                // 对照组：无save时毒药正常整局限一次
                var g = NewGame(seed + 900);
                int s0 = g.Current;
                int v0 = -1;
                for (int i = 0; i < 3; i++) if (i != s0 && g.Players[i].Alive && g.Players[i].Hand.Count > 0) { v0 = i; break; }
                if (v0 < 0) continue;
                var witch0 = new Card(9701, CardType.Witch);
                g.Players[s0].Hand.Add(witch0);
                g.ApplyAction(new GameAction { Kind = ActionKind.PoisonAttack, TargetSeat = v0, SourceCardId = 9701 });
                Check(witch0.PoisonUsed, "对照组：无save时毒药正常消耗（整局限一次）", "seed=" + seed);
                ctrlDone = true;
            }
            Check(freeDone, "save回合不消耗场景已验证");
            Check(ctrlDone, "对照组场景已验证");
        }

        // ------------------------------------------------------------------
        // 女巫解药复活窗口（2026-09-30）：只能救活上一回合内阵亡的角色牌（DiedStamp ≥ 我上一回合开始戳）
        // ------------------------------------------------------------------
        static void AntidoteFreshnessTest()
        {
            Console.WriteLine("== 女巫·解药：只能救活上一回合内阵亡的角色牌");
            bool done = false;
            for (int seed = 1; seed <= 30 && !done; seed++)
            {
                var e = NewGame(seed);
                int s0 = e.Current;
                if (e.IsSilenced(s0)) continue;
                var witch = new Card(9803, CardType.Witch);
                e.Players[s0].Hand.Add(witch);

                // 早先阵亡的（戳很旧）：救不了
                var stale = new Card(9802, CardType.Priest);
                stale.DiedStamp = e.CurrentStamp - 100;
                e.Discard.Add(stale);
                Check(!e.GetLegalActions(s0).Any(a => a.Kind == ActionKind.Antidote),
                    "弃牌堆只有早先阵亡的牌时，解药不可用（不白扣蓝）", "seed=" + seed);

                // 上一回合内阵亡的：救得活
                var fresh = new Card(9801, CardType.Priest);
                fresh.DiedStamp = e.CurrentStamp;
                e.Discard.Add(fresh);
                Check(e.GetLegalActions(s0).Any(a => a.Kind == ActionKind.Antidote),
                    "有上一回合内阵亡的牌时，解药可用", "seed=" + seed);
                e.ApplyAction(new GameAction { Kind = ActionKind.Antidote, SourceCardId = 9803 });
                Check(e.Players[s0].Hand.Any(c => c.Id == 9801), "救活的牌回到手牌", "seed=" + seed);
                Check(witch.AntidoteUsed, "解药整局一次（已消耗）", "seed=" + seed);
                Check(!e.GetLegalActions(s0).Any(a => a.Kind == ActionKind.Antidote && a.SourceCardId == 9803),
                    "同一张女巫的解药不可再用", "seed=" + seed);
                // 早先阵亡的仍留在弃牌堆
                Check(e.Discard.Any(c => c.Id == 9802), "早先阵亡的牌不会被解药捞走", "seed=" + seed);
                done = true;
            }
            Check(done, "解药复活窗口场景已验证");
        }

        // ------------------------------------------------------------------
        // 牺牲：牧师像侍卫一样挺身而出（优先挡刀的是侍卫）；对照组（无牺牲英雄）牧师不挡刀
        // 构造：受害者手牌=[牧师, 双药女巫]，被普攻——抽中女巫(9)应收 pastor 挡刀，抽中牧师(5)硬吃
        // ------------------------------------------------------------------
        static void PriestSacrificeTest()
        {
            Console.WriteLine("== 牺牲：牧师像侍卫一样挺身而出");
            int sacIntercepts = 0, sacPriestEaten = 0, ctrlPriestEaten = 0, ctrlWrongIntercept = 0, bothBodies = 0;
            int sacCases = 0, ctrlCases = 0;
            bool sacBlockDone = false;
            for (int seed = 1; seed <= 120 && (sacCases < 8 || ctrlCases < 8 || !sacBlockDone); seed++)
            {
                var e = NewGame(seed);
                int sac = FindHeroSeat(e, 6);
                int victim = sac >= 0 ? sac : (seed % 3);
                if (sac >= 0) sacCases++; else ctrlCases++;

                // ---- 牺牲牧师挡刀牺牲：亡语照样触发（2026-10-07 裁定，与圣骑士一致）----
                // 注意：必须放在主场景之前——主场景会清空受害者手牌（连英雄牌一起清掉），
                // 而挡刀判定需要受害者手牌里有英雄牌（抽中英雄→残血→AI 才同意牧师挡刀）。
                if (sac >= 0 && !sacBlockDone)
                {
                    int atk2 = (sac + 1) % 3;
                    if (e.Players[atk2].Alive)
                    {
                        e.Discard.Clear();   // 只留一张可回收牌，让 AI 的亡语选择确定
                        var deadW = new Card(9930 + seed, CardType.Witch);
                        e.Discard.Add(deadW);
                        var vp = e.Players[sac];
                        var keepHero = vp.Hand.FirstOrDefault(c => c.Type == CardType.Hero);
                        vp.Hand.Clear();
                        vp.Hand.Add(new Card(9940 + seed, CardType.Priest));
                        if (keepHero != null) vp.Hand.Add(keepHero);
                        vp.HP = 1;   // 残血：AI 同意用牧师挡刀
                        e.BasicAttackUsed = false;
                        e.Players[atk2].MP = 3;
                        bool priestBlocked = false;
                        e.OnLog += line => { if (line.Contains("【牧师】挺身而出")) priestBlocked = true; };
                        e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = sac });
                        if (priestBlocked && e.Discard.Any(c => c.Id == 9940 + seed))
                        {
                            Check(vp.Hand.Any(c => c.Id == 9930 + seed), "牺牲牧师挡刀牺牲：亡语照样回收弃牌堆的牌", "seed=" + seed);
                            if (vp.Hand.Any(c => c.Id == 9930 + seed)) sacBlockDone = true;
                        }
                        // 恢复主场景的前提：普攻名额与行动点
                        e.BasicAttackUsed = false;
                        e.Players[atk2].MP = 3;
                    }
                }

                // 构造受害者手牌：1牧师 + 1双药女巫（若受害者手牌原有侍卫则保留两张，验证"优先侍卫"）
                var victimHand = e.Players[victim].Hand;
                bool hadGuard = victimHand.Any(c => c.Type == CardType.Guard);
                victimHand.Clear();
                if (hadGuard) victimHand.Add(new Card(9900 + seed, CardType.Guard));
                victimHand.Add(new Card(9910 + seed, CardType.Priest));
                victimHand.Add(new Card(9920 + seed, CardType.Witch));   // 全新女巫=双药，ValueOf 9

                int attacker = (victim + 1) % 3;
                DriveToSeat(e, attacker);
                if (e.Current != attacker || e.GameOver) continue;
                bool intercepted = false, priestDied = false, guardDied = false, witchDied = false;
                e.OnLog += line =>
                {
                    if (line.Contains("【牧师】挺身而出")) intercepted = true;
                    if (line.Contains("【侍卫】挺身而出")) { intercepted = true; guardDied = true; }
                    if (line.Contains("【牧师】，进入弃牌堆")) priestDied = true;
                    if (line.Contains("【女巫】，进入弃牌堆")) witchDied = true;
                };
                e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = victim });

                if (sac >= 0)
                {
                    if (hadGuard)
                    {
                        // 有侍卫时优先侍卫：无论抽中什么，挡刀耗的都是侍卫（若AI选择挡）
                        if (guardDied) bothBodies++;
                        if (!witchDied && victimHand.Any(c => c.Type == CardType.Witch)) sacIntercepts++;
                    }
                    else if (intercepted && priestDied == false)
                    {
                        // 无侍卫：牧师挡下，女巫保住，牧师未"被抽走"
                        sacIntercepts++;
                    }
                    else if (priestDied) sacPriestEaten++;
                }
                else
                {
                    if (intercepted && !guardDied) ctrlWrongIntercept++;   // 对照组牧师绝不应挺身而出
                    if (priestDied) ctrlPriestEaten++;
                }
            }
            Console.WriteLine("  样本：牺牲组 " + sacCases + " 次攻击 / 对照组 " + ctrlCases + " 次攻击");
            Check(sacCases >= 8 && ctrlCases >= 8, "牺牲/对照样本量", sacCases + "/" + ctrlCases);
            Check(sacIntercepts + sacPriestEaten >= 3, "牺牲组挡刀或硬吃正常结算", "挡刀" + sacIntercepts + " 硬吃" + sacPriestEaten);
            Check(sacIntercepts >= 2, "牺牲组：抽中高价值牌时牧师确实挺身而出", sacIntercepts + "次");
            Check(ctrlWrongIntercept == 0, "对照组：无牺牲英雄时牧师绝不挡刀");
            Check(sacBlockDone, "牺牲牧师挡刀牺牲：亡语触发场景已验证");
        }

        // ------------------------------------------------------------------
        // 牺牲挡刀风格：style1 优先牧师 vs style2 优先侍卫（同一构造，对比挡刀身体）；
        // style3：只有"弃牌堆可回收价值 + 被挡牌价值 > 10"才挡（空弃牌堆+女巫=9 不挡；放军火库9 → 18 挡）
        // ------------------------------------------------------------------
        static void PriestSacStylesTest()
        {
            Console.WriteLine("== 牺牲挡刀风格：1优先牧师 / 2优先侍卫 / 3总EV>10才挡");
            int s1Priest = 0, s2Guard = 0, s3LowEvNoBlock = 0, s3HighEvBlock = 0, cases = 0;
            // 每次采样被抽中女巫才挡（p≈1/3），10 组样本对"≥2"阈值太晃；扩到 24 组把假失败压到 ~0.3%
            for (int seed = 1; seed <= 200 && cases < 24; seed++)
            {
                // --- style1 vs style2 身体优先序 ---
                var e = NewGame(seed);
                int sac = FindHeroSeat(e, 6);
                if (sac < 0) continue;
                int attacker = (sac + 1) % 3;
                DriveToSeat(e, attacker);
                if (e.Current != attacker || e.GameOver) continue;
                cases++;
                foreach (var kv in new[] { new[] { 1, 0 }, new[] { 2, 0 } })
                {
                    int style = kv[0];
                    e.PriestSacStyle = style;
                    e.BasicAttackUsed = false;
                    e.Players[attacker].MP = 3;
                    var hand = e.Players[sac].Hand;
                    hand.Clear();
                    hand.Add(new Card(9950 + seed * 10 + style, CardType.Guard));
                    hand.Add(new Card(9960 + seed * 10 + style, CardType.Priest));
                    hand.Add(new Card(9965 + seed * 10 + style, CardType.Witch));   // 双药女巫=9，AI同意挡刀
                    bool priestSacced = false, guardSacced = false;
                    e.OnLog += line =>
                    {
                        if (line.Contains("【牧师】挺身而出")) priestSacced = true;
                        if (line.Contains("【侍卫】挺身而出")) guardSacced = true;
                    };
                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = sac });
                    if (style == 1 && priestSacced) s1Priest++;
                    if (style == 2 && guardSacced) s2Guard++;
                }

                // --- style3 EV 阈值 ---
                var e3 = NewGame(seed + 5000);
                int sac3 = FindHeroSeat(e3, 6);
                if (sac3 < 0) continue;
                int atk3 = (sac3 + 1) % 3;
                DriveToSeat(e3, atk3);
                if (e3.Current != atk3 || e3.GameOver) continue;
                e3.PriestSacStyle = 3;
                e3.Discard.Clear();
                var h3 = e3.Players[sac3].Hand;
                h3.Clear();
                h3.Add(new Card(9970 + seed, CardType.Priest));
                h3.Add(new Card(9980 + seed, CardType.Witch));   // 双药女巫=9
                bool blocked = false;
                e3.OnLog += line => { if (line.Contains("挺身而出")) blocked = true; };
                e3.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = sac3 });   // 9 + 空弃牌堆0 = 9 ≤ 10 → 不挡
                if (!blocked) s3LowEvNoBlock++;
                e3.BasicAttackUsed = false;
                e3.Players[atk3].MP = 3;
                e3.Discard.Add(new Card(9990 + seed, CardType.Arsenal));   // 可回收9 + 女巫9 = 18 > 10 → 挡
                h3.Clear();
                h3.Add(new Card(9971 + seed, CardType.Priest));
                h3.Add(new Card(9981 + seed, CardType.Witch));
                blocked = false;
                e3.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = sac3 });
                if (blocked) s3HighEvBlock++;
            }
            Console.WriteLine("  样本 " + cases + " 组：style1牧师先挡 " + s1Priest + " / style2侍卫先挡 " + s2Guard + " / style3低EV不挡 " + s3LowEvNoBlock + " / style3高EV挡 " + s3HighEvBlock);
            Check(cases >= 6, "风格测试样本量", cases + "组");
            Check(s1Priest >= 2 && s2Guard >= 2, "style1优先牧师、style2优先侍卫", s1Priest + "/" + s2Guard);
            Check(s3LowEvNoBlock >= 1 && s3HighEvBlock >= 1, "style3按总EV>10判定挡刀", s3LowEvNoBlock + "/" + s3HighEvBlock);
        }

        // ------------------------------------------------------------------
        // 合成（2026-10-06）：1行动点+配方3张 → 2点生命合成牌+奖励抽1张；配方不齐/蓝不够则不合法
        // ------------------------------------------------------------------
        static void SynthCraftTest()
        {
            Console.WriteLine("== 合成：配方消耗/2点生命/奖励抽牌");
            bool done = false;
            for (int seed = 1; seed <= 20 && !done; seed++)
            {
                var e = NewGame(seed);
                int s0 = e.Current;
                var me = e.Players[s0];
                if (e.GameOver || me.MP < GameEngine2.COST_CRAFT) continue;
                // 构造女祭司配方：青苹果+女巫+牧师（再放一张大臣兜底"任意"位用不上——女祭司是全固定配方）
                var keepHero = me.Hand.FirstOrDefault(c => c.Type == CardType.Hero);
                foreach (var c in me.Hand.ToList()) { me.Hand.Remove(c); e.Discard.Add(c); }   // 原手牌进弃牌堆，保持卡牌守恒
                if (keepHero != null) me.Hand.Add(keepHero);   // 英雄牌永不离开手牌
                me.Hand.Add(new Card(8001, CardType.GreenApple));
                me.Hand.Add(new Card(8002, CardType.Witch));
                me.Hand.Add(new Card(8003, CardType.Priest));
                me.Hand.Add(new Card(8004, CardType.Minister));
                int totalBefore = e.TotalCards;
                int sideBefore = e.SideDeck.Count;
                int handBefore = me.Hand.Count;
                var legal = e.GetLegalActions(s0);
                Check(legal.Any(a => a.Kind == ActionKind.Craft && a.TargetType == CardType.Priestess),
                    "配方在手时合成女祭司合法", "seed=" + seed);
                Check(!legal.Any(a => a.Kind == ActionKind.Craft && a.TargetType == CardType.Valkyrie),
                    "配方不齐时其他合成不合法", "seed=" + seed);
                e.ApplyAction(new GameAction { Kind = ActionKind.Craft, TargetType = CardType.Priestess });
                var synth = me.Hand.FirstOrDefault(c => c.Type == CardType.Priestess);
                Check(synth != null, "合成牌进入手牌", "seed=" + seed);
                if (synth != null) Check(synth.SynthHP == GameEngine2.SYNTH_HP, "合成牌有2点生命", "seed=" + seed);
                Check(e.Discard.Count(c => c.Id == 8001 || c.Id == 8002 || c.Id == 8003) == 3, "配方3张进入弃牌堆", "seed=" + seed);
                Check(me.Hand.Any(c => c.Id == 8004), "配方外的牌不受影响", "seed=" + seed);
                // 2026-10-14 定稿：合成奖励=方案C护盾（CraftRewardStyle==2；==0时才回退为抽1张牌）
                Check(me.ShieldCharges == GameEngine2.CRAFT_SHIELD_CHARGES, "合成奖励：获得方案C护盾", "seed=" + seed);
                Check(me.ShieldExpireOwnTurn == me.OwnTurnCount + GameEngine2.CRAFT_SHIELD_TURNS,
                    "合成护盾持续2个自己回合", "seed=" + seed);
                Check(me.Hand.Count == handBefore - 2, "手牌数：-3配方 +1合成（奖励改为护盾不抽牌）", "seed=" + seed);
                // 合成牌是局内造出的牌，不计入物理牌库守恒（配方进弃牌堆仍算在内）→ 总数不变
                Check(e.TotalCards == totalBefore, "卡牌守恒：合成牌不计入TotalCards", "seed=" + seed);
                done = true;
            }
            Check(done, "合成场景已验证");
        }

        // ------------------------------------------------------------------
        // 合成"任意"位（2026-10-07）：女武神/圣骑士的第三张牌由玩家手动指定，引擎不再自动抓第一张
        // ------------------------------------------------------------------
        static void CraftWildcardTest()
        {
            Console.WriteLine("== 合成“任意”位：手动指定第三张牌");
            bool done = false;
            for (int seed = 1; seed <= 20 && !done; seed++)
            {
                var e = NewGame(seed);
                int s0 = e.Current;
                var me = e.Players[s0];
                if (e.GameOver || me.MP < GameEngine2.COST_CRAFT) continue;
                var keepHero = me.Hand.FirstOrDefault(c => c.Type == CardType.Hero);
                foreach (var c in me.Hand.ToList()) { me.Hand.Remove(c); e.Discard.Add(c); }   // 原手牌进弃牌堆，保持守恒
                if (keepHero != null) me.Hand.Add(keepHero);
                me.Hand.Add(new Card(8501, CardType.Guard));
                me.Hand.Add(new Card(8502, CardType.Maid));
                me.Hand.Add(new Card(8503, CardType.Minister));   // 候选"任意"位①
                me.Hand.Add(new Card(8504, CardType.Minister));   // 候选"任意"位②
                var legal = e.GetLegalActions(s0);
                var valk = legal.Where(a => a.Kind == ActionKind.Craft && a.TargetType == CardType.Valkyrie).ToList();
                Check(valk.Count == 2, "任意位候选=2张大臣，各生成1个合法行动", valk.Count + "个");
                Check(valk.All(a => a.SourceCardId == 8503 || a.SourceCardId == 8504), "合法行动只指向候选大臣", "");
                Check(!legal.Any(a => a.Kind == ActionKind.Craft && a.SourceCardId == 8501),
                    "固定槽的侍卫不能冒充任意位", "seed=" + seed);
                // 指定8504合成：消耗的是被点选的那张，不是引擎顺手抓的第一张
                e.ApplyAction(new GameAction { Kind = ActionKind.Craft, TargetType = CardType.Valkyrie, SourceCardId = 8504 });
                Check(me.Hand.Any(c => c.Type == CardType.Valkyrie), "女武神已合成", "seed=" + seed);
                Check(e.Discard.Any(c => c.Id == 8504), "被指定的第三张牌（8504）进弃牌堆", "seed=" + seed);
                Check(me.Hand.Any(c => c.Id == 8503), "未选中的候选牌（8503）留在手牌", "seed=" + seed);
                Check(e.Discard.Any(c => c.Id == 8501) && e.Discard.Any(c => c.Id == 8502), "固定槽侍卫+侍女被消耗", "seed=" + seed);
                // 英雄牌 / 不在手牌的 id 不能作为任意位
                if (keepHero != null)
                    Check(e.CraftCost(s0, CardType.Valkyrie, keepHero.Id) == null, "英雄牌不能作为任意位", "seed=" + seed);
                Check(e.CraftCost(s0, CardType.Valkyrie, 999999) == null, "不在手牌的id不能作为任意位", "seed=" + seed);
                done = true;
            }
            Check(done, "任意位选择场景已验证");
        }

        // ------------------------------------------------------------------
        // 女祭司双限定技：复活（从弃牌堆复活1张，限1次）/ 祈祷（2回合后自身1命→英雄1HP，限1次）
        // ------------------------------------------------------------------
        static void PriestessSkillsTest()
        {
            Console.WriteLine("== 女祭司：复活与祈祷（双限定技）");
            bool reviveDone = false, prayerDone = false;
            for (int seed = 1; seed <= 30 && !(reviveDone && prayerDone); seed++)
            {
                var e = NewGame(seed);
                int s0 = e.Current;
                var me = e.Players[s0];
                // ---- 复活 ----
                if (!reviveDone && me.MP >= GameEngine2.COST_SYNTH_SKILL)
                {
                    var dead = new Card(8101, CardType.Arsenal);
                    dead.DiedStamp = 1;   // 早先阵亡（女祭司复活不限时间，区别于女巫解药）
                    e.Discard.Add(dead);
                    var ps = new Card(8102, CardType.Priestess) { SynthHP = 2 };
                    me.Hand.Add(ps);
                    int logBefore = e.History.Count;
                    e.ApplyAction(new GameAction { Kind = ActionKind.SynthRevive, SourceCardId = 8102 });
                    Check(e.History.Count > logBefore, "复活已结算", "seed=" + seed);
                    Check(ps.ReviveUsed, "复活限1次：已置位", "seed=" + seed);
                    Check(me.Hand.Any(c => c.Id == 8101), "复活的目标回到手牌（AI自动选择）", "seed=" + seed);
                    Check(!e.GetLegalActions(s0).Any(a => a.Kind == ActionKind.SynthRevive && a.SourceCardId == 8102),
                        "复活不能再用（限定技）", "seed=" + seed);
                    reviveDone = true;
                }
                // ---- 祈祷 ----
                if (!prayerDone && me.MP >= GameEngine2.COST_SYNTH_SKILL)
                {
                    var ps2 = new Card(8202, CardType.Priestess) { SynthHP = 2 };
                    me.Hand.Add(ps2);
                    me.HP = 1;
                    // 清空对手手牌：他们不会攻击/偷走女祭司，专注验证延迟结算
                    foreach (var o in e.Players) if (o.Seat != s0) o.Hand.Clear();
                    int hpBefore = me.HP;
                    e.ApplyAction(new GameAction { Kind = ActionKind.SynthPrayer, SourceCardId = 8202 });
                    Check(ps2.PrayerUsed && ps2.PrayerDueTurn > 0, "祈祷已记录到期回合", "seed=" + seed);
                    Check(me.HP == hpBefore, "祈祷当回合不回血", "seed=" + seed);
                    // 过到自己下一个回合（OwnTurnCount+1）：不应触发
                    e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                    DriveToSeat(e, s0);
                    if (!e.GameOver && e.Current == s0)
                    {
                        Check(me.HP == hpBefore && ps2.SynthHP == 2, "第1个回合后不触发", "seed=" + seed);
                        e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                        DriveToSeat(e, s0);
                    }
                    if (!e.GameOver && e.Current == s0)
                    {
                        Check(me.HP == hpBefore + 1, "第2个回合后：英雄+1HP", "seed=" + seed);
                        Check(ps2.SynthHP == 1, "女祭司消耗1点生命（剩1）", "seed=" + seed);
                        Check(me.Hand.Any(c => c.Id == 8202), "还有生命：女祭司留在手牌", "seed=" + seed);
                    }
                    prayerDone = true;
                }
            }
            Check(reviveDone, "复活场景已验证");
            Check(prayerDone, "祈祷场景已验证");
        }

        // ------------------------------------------------------------------
        // 女武神/圣骑士：作为挡刀身体；女武神格挡军火库无损；圣骑士进弃牌堆（被抽走或挡刀消耗）都触发牧师亡语
        // ------------------------------------------------------------------
        static void SynthShieldTest()
        {
            Console.WriteLine("== 女武神：格挡军火库无损；圣骑士：挡刀+亡语");
            bool valkDone = false, palaDone = false, palaBlockDone = false;
            for (int seed = 1; seed <= 60 && !(valkDone && palaDone && palaBlockDone); seed++)
            {
                var e = NewGame(seed);
                int s0 = e.Current;              // 从初始行动方直接攻击，不轮转
                int victim = (s0 + 1) % 3;
                var v = e.Players[victim];
                if (e.GameOver || !v.Alive) continue;
                var atk = e.Players[s0];
                // ---- 女武神格挡军火库：回到手牌、不消耗 ----
                if (!valkDone && !e.IsSilenced(victim))
                {
                    var keepHero = v.Hand.FirstOrDefault(c => c.Type == CardType.Hero);
                    var valk = new Card(8301, CardType.Valkyrie) { SynthHP = 2 };
                    v.Hand.Clear();
                    v.Hand.Add(valk);
                    if (keepHero != null) v.Hand.Add(keepHero);   // 保留真英雄牌，保证抽中英雄→挡刀判定
                    v.HP = 1;   // 英雄被抽中必挡（残血才挡）
                    atk.Hand.Add(new Card(8302, CardType.Arsenal));
                    atk.MP = 3;
                    e.BasicAttackUsed = false;
                    int hpBefore = v.HP;
                    e.ApplyAction(new GameAction { Kind = ActionKind.ArsenalAttack, TargetSeat = victim, SourceCardId = 8302 });
                    if (v.Hand.Any(c => c.Id == 8301) && v.HP == hpBefore && e.Discard.All(c => c.Id != 8301))
                    {
                        Check(true, "女武神格挡军火库后毫发无损留在手牌", "seed=" + seed);
                        valkDone = true;
                    }
                    // 抽中女武神自己（非挡刀情形）时换种子重试
                }
                // ---- 圣骑士被抽走：触发牧师亡语（1点生命：直接抽中一次即耗尽）----
                if (!palaDone)
                {
                    var dead = new Card(8401, CardType.Witch);
                    e.Discard.Add(dead);
                    var pala = new Card(8402, CardType.Paladin) { SynthHP = 1 };
                    var keepHero2 = v.Hand.FirstOrDefault(c => c.Type == CardType.Hero);
                    v.Hand.Clear();
                    v.Hand.Add(pala);
                    if (keepHero2 != null) v.Hand.Add(keepHero2);
                    e.BasicAttackUsed = false;
                    atk.MP = 3;
                    int handBefore = v.Hand.Count;
                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = victim });
                    if (e.Discard.Any(c => c.Id == 8402))
                    {
                        // 圣骑士被抽走：进弃牌堆且触发亡语回收（-1圣骑士 +1回收 → 手牌数不变）
                        Check(v.Hand.Count == handBefore, "圣骑士被抽走：亡语回收1张（手牌数不变）", "seed=" + seed);
                        Check(v.Hand.Any(c => c.Id == 8401), "圣骑士亡语回收了弃牌堆的角色牌", "seed=" + seed);
                        palaDone = true;
                    }
                }
                // ---- 圣骑士挡刀被消耗：亡语同样触发（2026-10-07 裁定；1点生命：挡1刀即耗尽）----
                if (!palaBlockDone)
                {
                    var dead2 = new Card(8451, CardType.Witch);
                    e.Discard.Add(dead2);
                    var pala2 = new Card(8452, CardType.Paladin) { SynthHP = 1 };
                    var keepHero3 = v.Hand.FirstOrDefault(c => c.Type == CardType.Hero);
                    v.Hand.Clear();
                    v.Hand.Add(pala2);
                    if (keepHero3 != null) v.Hand.Add(keepHero3);
                    v.HP = 1;   // 残血：AI 同意用圣骑士挡刀
                    e.BasicAttackUsed = false;
                    atk.MP = 3;
                    bool blocked = false;
                    e.OnLog += line => { if (line.Contains("挺身而出") || line.Contains("代替进入")) blocked = true; };
                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = victim });
                    if (blocked && e.Discard.Any(c => c.Id == 8452))
                    {
                        Check(v.Hand.Any(c => c.Id == 8451), "圣骑士挡刀牺牲：亡语照样回收弃牌堆的牌", "seed=" + seed);
                        palaBlockDone = true;
                    }
                }
            }
            Check(valkDone, "女武神格挡军火库场景已验证");
            Check(palaDone, "圣骑士亡语场景已验证");
            Check(palaBlockDone, "圣骑士挡刀牺牲亡语场景已验证");
        }

        // ------------------------------------------------------------------
        // 典狱长·扣押：人质进自己手牌并封印；对方回合结束归还并解除封印
        // ------------------------------------------------------------------
        static void JailerHostageTest()
        {
            Console.WriteLine("== 典狱长：扣押人质与归还解封");
            bool done = false;
            for (int seed = 1; seed <= 30 && !done; seed++)
            {
                var e = NewGame(seed);
                int s0 = e.Current;
                var me = e.Players[s0];
                int victim = (s0 + 1) % 3;
                if (me.MP < GameEngine2.COST_SYNTH_SKILL || e.Players[victim].Hand.Count(x => x.Type != CardType.Hero) == 0) continue;
                me.Hand.Add(new Card(8501, CardType.Jailer) { SynthHP = 2 });
                int meHandBefore = me.Hand.Count;
                int victimHandBefore = e.Players[victim].Hand.Count;
                e.ApplyAction(new GameAction { Kind = ActionKind.SynthHostage, TargetSeat = victim, SourceCardId = 8501 });
                Check(e.Players[victim].Hand.Count == victimHandBefore - 1, "对方手牌-1（人质被带走）", "seed=" + seed);
                Check(me.Hand.Count == meHandBefore + 1, "人质进入典狱长手牌", "seed=" + seed);
                var hostage = me.Hand.FirstOrDefault(c => c.Id != 8501 && c.Type != CardType.Hero && e.CardSilenced(c));
                Check(hostage != null, "人质牌处于封印状态", "seed=" + seed);
                Check(!e.GetLegalActions(s0).Any(a => a.Kind == ActionKind.SynthHostage && a.SourceCardId == 8501),
                    "每回合限1次：同回合不能再次扣押", "seed=" + seed);
                // 触发归还：人质扣2回合——原拥有者第2个回合结束归还解封（2026-10-14）。
                // 只空过回合不驱动AI行动，避免第三方AI攻击抽走人质造成随机干扰
                int g2 = 0;
                while (hostage != null && !e.Players[victim].Hand.Any(c => c.Id == hostage.Id) && !e.GameOver && g2++ < 20)
                    e.ApplyAction(new GameAction { Kind = ActionKind.EndTurn });
                Check(hostage != null && e.Players[victim].Hand.Any(c => c.Id == hostage.Id), "对方第2个回合结束：人质归还", "seed=" + seed);
                Check(hostage == null || !e.CardSilenced(hostage), "归还同时解除封印", "seed=" + seed);
                done = true;
            }
            Check(done, "典狱长扣押场景已验证");
        }

        // ------------------------------------------------------------------
        // 审判官·审判：揭示异端（亡灵/女巫）处决；非异端放回
        // ------------------------------------------------------------------
        static void InquisitorJudgeTest()
        {
            Console.WriteLine("== 审判官：审判处决异端");
            bool execDone = false, spareDone = false;
            for (int seed = 1; seed <= 40 && !(execDone && spareDone); seed++)
            {
                var e = NewGame(seed);
                int s0 = e.Current;
                var me = e.Players[s0];
                if (me.MP < GameEngine2.COST_SYNTH_SKILL) continue;
                me.Hand.Add(new Card(8601, CardType.Inquisitor) { SynthHP = 2 });
                int victim = (s0 + 1) % 3;
                var v = e.Players[victim];
                if (v.Hand.Count == 0) continue;
                bool hasUndead = v.Hand.Any(c => c.Type == CardType.Undead);
                bool hasWitch = v.Hand.Any(c => c.Type == CardType.Witch);
                int vhandBefore = v.Hand.Count;
                int witchBefore = v.Hand.Count(c => c.Type == CardType.Witch);
                int undeadBefore = v.Hand.Count(c => c.Type == CardType.Undead);
                int logBefore = e.History.Count;
                e.ApplyAction(new GameAction { Kind = ActionKind.SynthJudge, TargetSeat = victim, SourceCardId = 8601 });
                Check(e.History.Count > logBefore, "审判已结算", "seed=" + seed);
                int witchAfter = v.Hand.Count(c => c.Type == CardType.Witch);
                int undeadAfter = v.Hand.Count(c => c.Type == CardType.Undead);
                if ((hasUndead && undeadAfter < undeadBefore) || (hasWitch && witchAfter < witchBefore))
                {
                    Check(e.History.Skip(logBefore).Any(m => m.Contains("异端") && m.Contains("处决")),
                        "异端被处决并记录日志", "seed=" + seed);
                    execDone = true;
                }
                else if (!hasUndead && !hasWitch)
                {
                    Check(v.Hand.Count == vhandBefore, "无异端：手牌不减少", "seed=" + seed);
                    Check(e.History.Skip(logBefore).Any(m => m.Contains("不是异端")), "非异端被放回", "seed=" + seed);
                    spareDone = true;
                }
            }
            Check(execDone, "审判处决异端场景已验证");
            Check(spareDone, "审判放回非异端场景已验证");
        }

        // ------------------------------------------------------------------
        // 英雄升级·试做（2026-10-19）：消灭8张牌升1级（限1次），奖励=方案C同款
        // 2回合1格护盾（吸收1次攻击）。计入：攻击抽走/挡刀消耗/处决/炸弹毁牌。
        // ------------------------------------------------------------------
        static void LevelUpShieldTest()
        {
            Console.WriteLine("== 英雄升级：8经验升1级，奖励2回合1格护盾");
            bool leveled = false, absorbed = false;
            for (int seed = 1; seed <= 30 && !(leveled && absorbed); seed++)
            {
                var e = NewGame(seed);
                int s0 = e.Current;
                var atk = e.Players[s0];
                int victim = (s0 + 1) % 3;
                var v = e.Players[victim];
                if (e.GameOver || !v.Alive || !atk.Alive) continue;
                // victim 手牌换成侍女（普攻必被消灭、不吃挡刀），英雄牌拿出暂存
                var vHero = v.Hand.FirstOrDefault(c => c.Type == CardType.Hero);
                foreach (var c in v.Hand) if (c.Type != CardType.Hero) e.Discard.Add(c);
                v.Hand.Clear();
                // 砍到升级为止：强攻等英雄一刀可能消灭2张，不按刀数断言；
                // 每刀前断言"升级前不发护盾、经验不超上限"
                int strikes = 0;
                while (atk.HeroLevel < 1 && strikes < 40)
                {
                    Check(atk.ShieldCharges == 0 && atk.EXP < GameEngine2.XP_LEVEL_2,
                        "升级前：不发护盾、经验未达升2门槛", "seed=" + seed + " EXP=" + atk.EXP + " 盾=" + atk.ShieldCharges);
                    if (!v.Hand.Any(c => c.Type == CardType.Maid))
                        v.Hand.Add(new Card(9200 + strikes, CardType.Maid));   // 补充刀靶
                    e.BasicAttackUsed = false;
                    atk.MP = 3;
                    e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = victim });
                    strikes++;
                }
                if (atk.HeroLevel != 1 || atk.EXP != GameEngine2.XP_LEVEL_2) continue;   // 本种子没升到2级，换种子
                Check(atk.ShieldCharges == 1, "升2级：获得1格护盾", "seed=" + seed);
                Check(atk.ShieldExpireOwnTurn == atk.OwnTurnCount + GameEngine2.CRAFT_SHIELD_TURNS,
                    "护盾持续2个自己回合", "seed=" + seed);
                leveled = true;
                // 护盾吸收：行动权临时给 victim，令其攻击 s0（s0 手牌只留英雄牌，必抽本体）。
                // 强攻/攻城锤等英雄可能一刀多次攻击：预置5格护盾，按日志计数吸收次数
                var aHero = atk.Hand.FirstOrDefault(c => c.Type == CardType.Hero);
                foreach (var c in atk.Hand) if (c.Type != CardType.Hero) e.Discard.Add(c);
                atk.Hand.Clear();
                if (aHero != null) atk.Hand.Add(aHero);   // 英雄牌永不离开手牌
                if (vHero != null) v.Hand.Add(vHero);   // 还回 victim 英雄牌，防其空手被 elim 规则误伤
                if (aHero == null) continue;
                atk.ShieldCharges = 5;
                int hpBefore = atk.HP;
                int logMark = e.History.Count;
                e.Current = victim;   // public 字段：让 victim 成为行动方发动攻击
                e.BasicAttackUsed = false;
                v.MP = 3;
                e.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = s0 });
                int absorbedCnt = e.History.Skip(logMark).Count(m => m.Contains("吸收了这次攻击"));
                Check(absorbedCnt >= 1, "护盾吸收攻击（有播报）", "seed=" + seed);
                Check(atk.HP == hpBefore, "护盾吸收攻击：英雄HP不掉", "seed=" + seed + " absorbed=" + absorbedCnt);
                Check(atk.ShieldCharges == 5 - absorbedCnt, "护盾按吸收次数扣减", "seed=" + seed);
                absorbed = true;
            }
            Check(leveled, "升级场景已验证");
            Check(absorbed, "护盾吸收场景已验证");
        }
    }
}
