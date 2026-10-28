using System;
using System.Collections.Generic;
using System.Linq;

namespace SandKill.Core
{
    /// <summary>
    /// 3代引擎「法术迸发」（2026-10-20 用户设计）：在2代（GameEngine2）全部规则之上加法术系统。
    /// 与2代并存：模式选择分别构造 GameEngine2 / GameEngine3，互不影响。
    ///
    /// 法术通用规则：
    ///   · 法术牌释放后直接消耗（进弃牌堆）。牌库张数（2026-10-21 用户裁定）：炼金术×1 / 清晰术×2 / 点燃×3 / 雷霆×3 / 彗星×3 / 屏障×3（由 deckCounts 决定）。
    ///   · 未学会前是纯白板（不可释放）。
    ///   · 炼金术×1=通用法术（2026-10-22 中午用户重做，取代"从牌库回收"版——回收要再花1行动点释放，白白浪费）：
    ///     释放时选择一种已研习法术（不含炼金术自身/彗星），当场以该法术结算，只付炼金术这1点行动点；"贵的东西缺点只有贵"。
    ///   · 升级奖励（2026-10-21）：消灭6张升2级、8张升3级，每局最多2次；3代升2级=1技能点+回1点英雄HP，升3级=1技能点+回1点英雄HP（2026-10-21 用户试行：升2也回血）。
    ///   · learn 行动：1 技能点 + 1 行动点，点亮一种法术的释放权限；下个自己的回合开始才学会。
    ///   · 彗星反制：有人释放法术时，按座位顺序询问每个已研习彗星、且持有彗星的对手（真人弹 CometCounter 抉择，
    ///     AI 只对指向自己的威胁法术反制）；1 张彗星抵消整个法术（法术仍消耗、无效果）。未研习者不参与询问。
    ///
    /// 已知取舍（TODO，待与用户确认）：
    ///   · ~~清晰术抽牌2选1~~（2026-10-21 清晰术已重做：改为看库顶3张选1张入手，无此需求）。
    ///   · 旧版"清晰术标记对手明置"机制代码保留（IsMarked/MarkedCards/AttackMarked），现已无来源，永远空转。
    /// </summary>
    public class GameEngine3 : GameEngine2
    {
        // ---- 法术常量 ----
        public const int COST_LEARN = 1;        // 学习：1行动点
        public const int COST_CAST = 1;         // 释放：1行动点（彗星作为反制消耗0）
        public const int IGNITE_DELAY = 2;        // 点燃：2回合后阵亡（以释放者的 OwnTurnCount 为节拍，与炸弹同口径）

        public static readonly CardType[] SpellTypes =
            { CardType.Alchemy, CardType.Clarity, CardType.Ignite, CardType.Thunder, CardType.Comet, CardType.Barrier };

        public static bool IsSpellType(CardType t) { return t >= CardType.Alchemy && t <= CardType.Barrier; }
        static int SpellIdx(CardType t) { return t - CardType.Alchemy; }

        /// <summary>3代初始手牌=10张（比1/2代多1）：法术牌入库后原有牌抽率被稀释，+1补偿（2026-10-21 用户裁定）。</summary>
        protected override int StartHandSize { get { return 10; } }

        // ---- 3代局内状态 ----
        readonly bool[,] _known;                    // [seat, 法术序号] 释放权限
        readonly int[] _learningSpell;              // 正在研习的法术（CardType 转 int，0=没有在学）
        readonly int[] _learningDueTurn;            // 学会时点 = 自己的第 N 个回合开始
        readonly List<ClarityMark> _marks = new List<ClarityMark>();     // 清晰术标记（暗置→明置）
        readonly List<IgniteMark> _ignites = new List<IgniteMark>();     // 点燃标记
        readonly bool[] _clarityNextDrawChoice;     // 清晰术奖励：下次抽牌2选1（见类注释 TODO）
        // 彗星反制暂停上下文（Pending=CometCounter 时有效）
        GameAction _counterAction;                  // 被反制的释放行动（恢复后结算用）
        Card _counterSpellCard;                     // 被反制的法术牌（已消耗，仅播报/校验用）
        CardType _counterEffective;                 // 实际结算的法术（炼金术=被模仿的法术；2026-10-22 中午重做）
        int _counterCaster = -1;
        bool _counterResume;                        // true=询问完所有人无反制，提交后恢复结算

        class ClarityMark { public Card Card; public int OwnerSeat; }
        class IgniteMark { public Card Card; public int OwnerSeat; public int CasterSeat; public int DueTurn; }

        public GameEngine3(Dictionary<CardType, int> deckCounts, string[] names, List<IPlayerBrain2> brains, int seed,
                           HeroSpec[] forceHeroes = null, int[] startTickets = null)
            : base(deckCounts, names, brains, seed, forceHeroes, startTickets)
        {
            int n = names.Length;
            _known = new bool[n, SpellTypes.Length];
            _learningSpell = new int[n];
            _learningDueTurn = new int[n];
            _clarityNextDrawChoice = new bool[n];
        }

        // ==================================================================
        // 升级奖励（2026-10-21 用户裁定）：3代 升2级 = 1 技能点 + 回1点英雄HP；升3级 = 1 技能点 + 回1点英雄HP（2026-10-21 用户试行：升2也回血）
        // ==================================================================
        protected override void GrantLevelUpReward(PlayerState p, int level)
        {
            p.SkillPoints++;
            HealHero(p, 1);
        }
        protected override string LevelUpRewardText(int level)
        {
            return "获得 1 个【技能点】，并回复 1 点英雄HP！";
        }

        // ==================================================================
        // 查询接口（UI / AI / 测试）
        // ==================================================================
        /// <summary>seat 是否已学会法术 t 的释放。</summary>
        public bool HasSpell(int seat, CardType t) { return IsSpellType(t) && _known[seat, SpellIdx(t)]; }

        /// <summary>seat 正在研习的法术（0=没有在学）。</summary>
        public CardType LearningSpell(int seat) { return (CardType)_learningSpell[seat]; }

        /// <summary>被清晰术标记、因而明置的牌（某座位手牌中）。</summary>
        public List<Card> MarkedCards(int seat)
        {
            return _marks.Where(m => m.OwnerSeat == seat).Select(m => m.Card).ToList();
        }

        /// <summary>该牌是否被清晰术标记（明置）。</summary>
        public bool IsMarked(Card c) { return _marks.Any(m => m.Card == c); }

        /// <summary>被点燃的牌与剩余回合（释放者节拍）。UI/测试用。</summary>
        public int IgniteRemaining(Card c)
        {
            var ig = _ignites.FirstOrDefault(x => x.Card == c);
            if (ig == null) return -1;
            return Math.Max(0, ig.DueTurn - Players[ig.CasterSeat].OwnTurnCount);
        }

        // ==================================================================
        // 回合开始钩子：学会完成 + 点燃到点 + 标记清理
        // ==================================================================
        protected override void StartTurn(int seat)
        {
            base.StartTurn(seat);
            var p = Players[seat];
            // 学会：上个自己回合开始研习 → 本回合开始生效
            // 防御基类构造期调用：base 构造里可能直接 StartTurn(0)（无人持调度券的局），
            // 此时本类数组尚未分配（3代约8%的局会踩中，直接开局崩溃），构造期不可能有研习中，跳过即可。
            if (_learningSpell != null && _learningSpell[seat] != 0 && p.OwnTurnCount >= _learningDueTurn[seat])
            {
                var t = (CardType)_learningSpell[seat];
                _known[seat, SpellIdx(t)] = true;
                _learningSpell[seat] = 0;
                Log(p.Name + " 学会了【" + t.Name() + "】的释放！");
            }
            TickIgnites();
            PruneMarks();
        }

        // ==================================================================
        // 行动入口
        // ==================================================================
        public override void ApplyAction(GameAction a)
        {
            if (InMulligan) { base.ApplyAction(a); return; }
            if (GameOver || Pending != null) return;
            // 清晰术奖励的"下次抽牌2选1"提交：作为 PickFromDiscard 之外的专用提交走 SubmitClarityPick
            if (a.Kind == ActionKind.Learn) { DoLearn(a); return; }
            if (a.Kind == ActionKind.CastSpell) { DoCast(a); return; }
            // 攻击被清晰术标记的牌：临时换库让 base 必抽中该牌，结算后恢复
            if (a.Kind == ActionKind.Attack && a.SecondCardId > 0) { AttackMarked(a); return; }
            base.ApplyAction(a);
            PruneMarks();
        }

        void DoLearn(GameAction a)
        {
            int seat = Current;
            var me = Players[seat];
            if (IsSilenced(seat)) return;   // 与2代技能同口径：被沉默全套时不可使用技能
            if (!IsSpellType(a.TargetType)) return;
            if (me.SkillPoints <= 0 || _learningSpell[seat] != 0) return;
            if (_known[seat, SpellIdx(a.TargetType)]) return;      // 已会
            if (me.MP < COST_LEARN) return;
            me.MP -= COST_LEARN;
            me.SkillPoints--;
            _learningSpell[seat] = (int)a.TargetType;
            _learningDueTurn[seat] = me.OwnTurnCount + 1;          // 下个自己的回合开始学会
            Log(me.Name + " 消耗1技能点+1行动点，开始研习【" + a.TargetType.Name()
                + "】（白板期间不可释放；下个自己的回合学会）。");
        }

        // ==================================================================
        // 释放法术
        // ==================================================================
        void DoCast(GameAction a)
        {
            int seat = Current;
            var me = Players[seat];
            if (IsSilenced(seat)) return;   // 与2代技能同口径
            var card = me.Hand.FirstOrDefault(c => c.Id == a.SourceCardId && IsSpellType(c.Type));
            if (card == null) return;
            if (!HasSpell(seat, card.Type))
            {
                Log(me.Name + " 的【" + card.Type.Name() + "】还没学会，无法释放（纯白板）。");
                return;
            }
            // 2026-10-22 中午用户重做（"加强炼金术"）：炼金术=通用法术——直接以已研习的任一种法术当场结算。
            // 不再"从牌库回收"：回收要再花1行动点释放，白白浪费行动点；"贵的东西缺点只有贵"。
            CardType effective = card.Type;
            if (card.Type == CardType.Alchemy)
            {
                if (!IsSpellType(a.TargetType) || a.TargetType == CardType.Alchemy || a.TargetType == CardType.Comet
                    || !HasSpell(seat, a.TargetType))
                {
                    Log(me.Name + " 的【炼金术】需要选择一种已研习的法术来当场化作（不含炼金术自身；彗星是被动反制，不可模仿）。");
                    return;
                }
                effective = a.TargetType;
            }
            int cost = card.Type == CardType.Comet ? 0 : COST_CAST;
            if (me.MP < cost) return;
            if (!ValidateTargets(effective, seat, a, out string why)) { Log("【" + effective.Name() + "】" + why); return; }

            // 先付代价+消耗（被彗星抵消也不退还）
            me.MP -= cost;
            me.Hand.Remove(card);
            DiscardNow(card);
            Log(me.Name + " 释放【" + card.Type.Name() + "】"
                + (effective != card.Type ? "——当场化作【" + effective.Name() + "】！" : "！"));

            // 彗星反制窗口：按座位顺序问每个持彗星的对手（炼金术以被模仿的法术参与威胁判定）
            if (TryCometCounter(seat, card, effective, a)) return;
            ResolveSpell(effective, seat, a);
        }

        /// <summary>各法术的目标校验（ why=失败原因 ）。</summary>
        bool ValidateTargets(CardType spell, int seat, GameAction a, out string why)
        {
            why = "";
            var me = Players[seat];
            switch (spell)
            {
                case CardType.Ignite:
                    // 2026-10-21② 用户强化：目标=一名还有非英雄牌的对手（具体点燃哪张在释放时2张里选，不在行动层面）
                    if (a.TargetSeat < 0 || a.TargetSeat >= Players.Count || a.TargetSeat == seat
                        || !Players[a.TargetSeat].Alive
                        || !Players[a.TargetSeat].Hand.Any(c => c.Type != CardType.Hero && !IsMarked(c)))
                        why = "：需要选择一个有可点燃手牌的对手";
                    break;
                case CardType.Thunder:
                    if (a.TargetSeat < 0 || a.TargetSeat >= Players.Count || a.TargetSeat == seat
                        || !Players[a.TargetSeat].Alive || Players[a.TargetSeat].Hand.Count == 0)
                        why = "：需要选择一个有手牌的对手";
                    break;
            }
            return why == "";
        }

        int OwnerOf(Card c) { return Players.First(p => p.Hand.Contains(c)).Seat; }

        // ==================================================================
        // 彗星反制
        // ==================================================================
        bool TryCometCounter(int casterSeat, Card spellCard, CardType effective, GameAction a)
        {
            foreach (var p in Players)
            {
                if (p.Seat == casterSeat || !p.Alive) continue;
                if (!HasSpell(p.Seat, CardType.Comet)) continue;   // 2026-10-21 用户裁定：彗星是法术牌，反制需先研习
                if (!p.Hand.Any(c => c.Type == CardType.Comet)) continue;
                if (p.IsHuman)
                {
                    // 真人：弹抉择，暂停；提交后从断点继续
                    _counterCaster = casterSeat;
                    _counterSpellCard = spellCard;
                    _counterEffective = effective;
                    _counterAction = CloneAction(a);
                    _counterResume = true;
                    Pending = new PendingChoice
                    {
                        Kind = ChoiceKind.CometCounter,
                        Seat = p.Seat,
                        DrawnCard = spellCard,
                        AttackerSeat = casterSeat,
                        Reason = effective.Name()
                    };
                    return true;
                }
                // AI：只对指向自己的威胁法术反制（点燃/雷霆指向自己，或清晰术标记了自己的牌）；炼金术以被模仿的法术判定
                bool threat = (effective == CardType.Ignite || effective == CardType.Thunder) && a.TargetSeat == p.Seat
                              || (effective == CardType.Clarity
                                  && (OwnerOfId(a.SecondCardId) == p.Seat || OwnerOfId(a.ThirdCardId) == p.Seat));
                if (threat) { ConsumeComet(p.Seat, effective, casterSeat); return true; }
            }
            return false;
        }

        int OwnerOfId(int cardId)
        {
            foreach (var p in Players) if (p.Hand.Any(c => c.Id == cardId)) return p.Seat;
            return -1;
        }

        void ConsumeComet(int responderSeat, CardType spellType, int casterSeat)
        {
            var comet = Players[responderSeat].Hand.FirstOrDefault(c => c.Type == CardType.Comet);
            if (comet == null) return;
            Players[responderSeat].Hand.Remove(comet);
            DiscardNow(comet);
            Log(Players[responderSeat].Name + " 消耗【彗星】，抵消了 " + Players[casterSeat].Name + " 的【" + spellType.Name() + "】！");
        }

        static GameAction CloneAction(GameAction a)
        {
            return new GameAction
            {
                Kind = a.Kind, TargetSeat = a.TargetSeat, SourceCardId = a.SourceCardId,
                TargetType = a.TargetType, SecondCardId = a.SecondCardId, ThirdCardId = a.ThirdCardId
            };
        }

        /// <summary>真人提交彗星反制抉择（UI 调用）。counter=true 消耗1张彗星抵消；false 放弃。</summary>
        public void SubmitCometChoice(bool counter)
        {
            if (Pending == null || Pending.Kind != ChoiceKind.CometCounter) return;
            int responder = Pending.Seat;
            var spellCard = _counterSpellCard;
            var action = _counterAction;
            int caster = _counterCaster;
            var effective = _counterEffective;
            bool resume = _counterResume;
            Pending = null;
            _counterResume = false;
            if (counter)
            {
                ConsumeComet(responder, effective, caster);
                return;   // 被抵消：法术无效果（代价已付、牌已消耗）
            }
            // 放弃：继续问后面的对手？简化口径：一次一人，放弃即问下一个持彗星者
            if (TryCometCounterNext(responder, spellCard, action)) return;
            if (resume) ResolveSpell(effective, caster, action);
        }

        // ------------------------------------------------------------------
        // 清晰术选牌（2026-10-21 重做）
        // ------------------------------------------------------------------

        /// <summary>真人提交清晰术选牌（UI 调用）：从牌库顶3张（Options）中选1张加入手牌，其余按原序放回库顶。</summary>
        public void SubmitClarityPick(int cardId)
        {
            var pc = Pending;
            if (pc == null || pc.Kind != ChoiceKind.ClarityPick) return;
            var pick = pc.Options.FirstOrDefault(c => c.Id == cardId);
            if (pick == null) return;
            Pending = null;
            FinishClarityPick(Players[pc.Seat], pc.Options, pick);
        }

        void FinishClarityPick(PlayerState me, List<Card> top, Card pick)
        {
            var rest = top.Where(c => c != pick).ToList();
            foreach (var c in top) SideDeck.Remove(c);
            me.Hand.Add(pick);
            SideDeck.InsertRange(0, rest);   // 其余按原序放回库顶
            Log(me.Name + " 的【清晰术】窥视了牌库顶 " + top.Count + " 张牌，拿走了【" + pick.Type.Name() + "】。");
        }

        // ------------------------------------------------------------------
        // 点燃选牌（2026-10-21② 用户强化）：查看2张 → 选1张挂上点燃标记
        // ------------------------------------------------------------------

        /// <summary>真人提交点燃选牌（UI 调用）：从亮出的2张（Options）中选1张点燃。</summary>
        public void SubmitIgnitePick(int cardId)
        {
            var pc = Pending;
            if (pc == null || pc.Kind != ChoiceKind.IgnitePick) return;
            var pick = pc.Options.FirstOrDefault(c => c.Id == cardId);
            Pending = null;
            if (pick == null) return;
            FinishIgnite(Players[pc.Seat], Players[pc.AttackerSeat], pick);
        }

        void FinishIgnite(PlayerState caster, PlayerState victim, Card pick)
        {
            // 安全兜底：选择时点与亮出点之间牌已不在其手（暂停期间被护住等）→ 换一张可点的；没有则落空
            if (!victim.Hand.Contains(pick))
            {
                pick = victim.Hand.FirstOrDefault(c => c.Type != CardType.Hero && !IsMarked(c));
                if (pick == null) { Log("【点燃】目标失效，没有效果。"); return; }
            }
            _ignites.Add(new IgniteMark
            {
                Card = pick,
                OwnerSeat = victim.Seat,
                CasterSeat = caster.Seat,
                DueTurn = caster.OwnTurnCount + IGNITE_DELAY
            });
            Log(caster.Name + " 的【点燃】缠上了 " + victim.Name + " 的【" + pick.Type.Name()
                + "】——" + IGNITE_DELAY + " 回合后这张牌将阵亡（其手牌中的军火库会同步炸毁）。");
        }

        /// <summary>从某个座位之后继续询问彗星反制（ true=又暂停/抵消 ）。</summary>
        bool TryCometCounterNext(int afterSeat, Card spellCard, GameAction action)
        {
            // k < Count：绕回 afterSeat 自己时停——放弃过的人不再问第二次
            for (int k = 1; k < Players.Count; k++)
            {
                int s = (afterSeat + k) % Players.Count;
                if (s == _counterCaster || !Players[s].Alive) continue;
                var p = Players[s];
                if (!HasSpell(s, CardType.Comet)) continue;   // 2026-10-21 用户裁定：反制需先研习彗星
                if (!p.Hand.Any(c => c.Type == CardType.Comet)) continue;
                if (p.IsHuman)
                {
                    Pending = new PendingChoice
                    {
                        Kind = ChoiceKind.CometCounter,
                        Seat = p.Seat,
                        DrawnCard = spellCard,
                        AttackerSeat = _counterCaster,
                        Reason = spellCard.Type.Name()
                    };
                    return true;
                }
                bool threat = (_counterEffective == CardType.Ignite || _counterEffective == CardType.Thunder) && action.TargetSeat == p.Seat
                              || (_counterEffective == CardType.Clarity
                                  && (OwnerOfId(action.SecondCardId) == p.Seat || OwnerOfId(action.ThirdCardId) == p.Seat));
                if (threat) { ConsumeComet(p.Seat, _counterEffective, _counterCaster); return true; }
            }
            return false;
        }

        // ==================================================================
        // 法术结算
        // ==================================================================
        void ResolveSpell(CardType spell, int seat, GameAction a)
        {
            var me = Players[seat];
            switch (spell)
            {
                case CardType.Clarity:
                    {
                        // 清晰术（2026-10-21 用户重做）：消耗1行动点，立刻看牌库顶的3张牌，从中选1张加入手牌
                        int n = Math.Min(3, SideDeck.Count);
                        if (n == 0) { Log("【清晰术】牌库已空，没有效果。"); break; }
                        var top = SideDeck.Take(n).ToList();
                        if (me.IsHuman)
                        {
                            Pending = new PendingChoice { Kind = ChoiceKind.ClarityPick, Seat = seat, Options = top, Reason = "清晰术" };
                        }
                        else
                        {
                            int pickId = Brains[seat].ChooseFromDiscard(this, seat, top);
                            var pick = top.FirstOrDefault(c => c.Id == pickId) ?? top[0];
                            FinishClarityPick(me, top, pick);
                        }
                        break;
                    }
                case CardType.Ignite:
                    {
                        // 点燃（2026-10-21② 用户强化）：查看目标对手2张手牌，选1张点燃——
                        // 不再是指定/默认第一张，"看2选1"保证点掉的是真威胁（如军火库），延迟2回合的代价才有对价。
                        // 2026-10-21③ 用户裁定·军火库克制：对持有军火库的玩家释放时，军火库【当场爆炸】（瞬时兑现，
                        // 不留给对方2回合继续用/转移的时间）——爆炸=直接进弃牌堆、不触发军火库攻击效果（与原"同步炸毁"同口径）、
                        // 不计消灭经验；基础效果（看2选1延迟烧毁）照常叠加在其后。
                        var v = Players[a.TargetSeat];
                        if (!v.Alive) { Log("【点燃】目标失效，没有效果。"); break; }
                        var arsenal = v.Hand.FirstOrDefault(c => c.Type == CardType.Arsenal);
                        if (arsenal != null)
                        {
                            v.Hand.Remove(arsenal);
                            DiscardNow(arsenal);
                            Log(me.Name + " 的【点燃】直接引爆了 " + v.Name + " 手牌中的【军火库】——冲击之下，化为废铁！");
                        }
                        var pool = v.Hand.Where(c => c.Type != CardType.Hero && !IsMarked(c)).ToList();
                        if (pool.Count == 0)
                        {
                            Log(arsenal != null ? "【点燃】军火库已爆，对方其余手牌没有可点燃的牌。" : "【点燃】对方没有可点燃的牌，没有效果。");
                            break;
                        }
                        // 随机亮2张（不足2张全亮）
                        var reveal = new List<Card>();
                        var tmp = pool.ToList();
                        while (reveal.Count < 2 && tmp.Count > 0)
                        {
                            var c = tmp[Rnd(tmp.Count)];
                            tmp.Remove(c);
                            reveal.Add(c);
                        }
                        if (me.IsHuman)
                        {
                            Pending = new PendingChoice { Kind = ChoiceKind.IgnitePick, Seat = seat, Options = reveal,
                                AttackerSeat = v.Seat, Reason = v.Name };
                        }
                        else
                        {
                            int pickId = Brains[seat].ChooseFromDiscard(this, seat, reveal);
                            var pick = reveal.FirstOrDefault(c => c.Id == pickId) ?? reveal[0];
                            FinishIgnite(me, v, pick);
                        }
                        break;
                    }
                case CardType.Thunder:
                    {
                        var v = Players[a.TargetSeat];
                        int times = (int)Math.Round(v.Hand.Count * 0.25, MidpointRounding.AwayFromZero);   // 25% 四舍五入
                        Log(me.Name + " 的【雷霆】轰向 " + v.Name + "：" + v.Hand.Count + " 张手牌 × 25% = " + times + " 次攻击！");
                        for (int i = 0; i < times && !GameOver; i++)
                        {
                            if (!v.Alive || v.Hand.Count == 0) break;
                            if (Pending != null) break;                // 真人弹了别的抉择就停手（不透支）
                            BasicAttackUsed = false;                   // 雷霆的每次攻击独立，不占/不受普攻限制
                            if (me.MP < COST_BASIC_ATTACK) me.MP = COST_BASIC_ATTACK;   // 雷霆总共只收1行动点：子攻击不再收费
                            base.ApplyAction(new GameAction { Kind = ActionKind.Attack, TargetSeat = a.TargetSeat });
                            me.MP += COST_BASIC_ATTACK;                // 退还子攻击的蓝耗
                        }
                        break;
                    }
                case CardType.Barrier:
                    {
                        // 屏障（2026-10-21 用户新增）：1行动点获得1点持续2回合的护盾（与合成/升级护盾同口径）
                        me.ShieldCharges += 1;
                        me.ShieldExpireOwnTurn = me.OwnTurnCount + LEVEL_SHIELD_TURNS;
                        Log(me.Name + " 的【屏障】展开：获得 1 点护盾，持续 " + LEVEL_SHIELD_TURNS + " 回合（可吸收 1 点伤害，保护本体与整个手牌）。");
                        break;
                    }
            }
        }

        // ==================================================================
        // 点燃计时 / 标记清理
        // ==================================================================
        void TickIgnites()
        {
            foreach (var ig in _ignites.ToList())
            {
                var caster = Players[ig.CasterSeat];
                var owner = Players[ig.OwnerSeat];
                // 牌已不在原拥有者手牌（被用掉/被抢走/拥有者出局）→ 点燃落空
                if (!owner.Hand.Contains(ig.Card) || !owner.Alive) { _ignites.Remove(ig); continue; }
                if (!caster.Alive) { _ignites.Remove(ig); continue; }   // 释放者出局，点燃熄灭
                if (caster.OwnTurnCount < ig.DueTurn) continue;
                _ignites.Remove(ig);
                Log("【点燃】到点：" + owner.Name + " 的【" + ig.Card.Type.Name() + "】被烧毁！");
                KillCardAsAttack(ig.CasterSeat, ig.OwnerSeat, ig.Card);
                // 军火库同步炸毁（不触发军火库的攻击效果）
                var arsenal = owner.Hand.FirstOrDefault(c => c.Type == CardType.Arsenal);
                if (arsenal != null)
                {
                    owner.Hand.Remove(arsenal);
                    DiscardNow(arsenal);
                    Log(owner.Name + " 手牌中的【军火库】被【点燃】波及，同步炸毁！");
                }
            }
        }

        void PruneMarks()
        {
            _marks.RemoveAll(m => !Players[m.OwnerSeat].Hand.Contains(m.Card));
        }

        // ==================================================================
        // 攻击被清晰术标记的牌
        // ==================================================================
        void AttackMarked(GameAction a)
        {
            var v = Players[a.TargetSeat];
            var marked = v.Alive ? v.Hand.FirstOrDefault(c => c.Id == a.SecondCardId) : null;
            if (marked == null || !IsMarked(marked)) { base.ApplyAction(a); return; }
            // 换库：只留被标记的牌，base 攻击必抽中它；结算（或暂停）后恢复原手牌
            var stash = v.Hand.Where(c => c.Id != marked.Id).ToList();
            v.Hand.Clear();
            v.Hand.Add(marked);
            base.ApplyAction(a);
            v.Hand.Remove(marked);                     // 若已被消耗/抢走则不在
            foreach (var c in stash) if (!v.Hand.Contains(c)) v.Hand.Add(c);
            PruneMarks();
        }

        // ==================================================================
        // 合法行动：base + learn/释放法术/攻击标记牌
        // ==================================================================
        public override List<GameAction> GetLegalActions(int seat)
        {
            var list = base.GetLegalActions(seat);
            if (InMulligan || GameOver || Pending != null) return list;
            if (seat != Current) return list;
            var me = Players[seat];
            if (!me.Alive) return list;
            bool silenced = IsSilenced(seat);

            // learn：有技能点、没在学、还有没学会的法术（彗星也算——2026-10-21 用户裁定：彗星是法术牌，反制同样需要研习）
            if (!silenced && me.SkillPoints > 0 && _learningSpell[seat] == 0 && me.MP >= COST_LEARN)
                foreach (var t in SpellTypes)
                    if (!_known[seat, SpellIdx(t)])
                        list.Add(new GameAction { Kind = ActionKind.Learn, TargetType = t });

            // 释放法术
            if (!silenced)
                foreach (var card in me.Hand.Where(c => IsSpellType(c.Type)))
                {
                    if (!HasSpell(seat, card.Type)) continue;
                    int cost = card.Type == CardType.Comet ? 0 : COST_CAST;
                    if (me.MP < cost) continue;
                    switch (card.Type)
                    {
                        case CardType.Alchemy:
                            // 2026-10-22 中午重做：通用法术——直接化作已研习的任一种法术当场结算。
                            // TargetType=被模仿的法术（点燃/雷霆需带目标）；不含炼金术自身与彗星（彗星是被动反制）。
                            foreach (var t in SpellTypes)
                            {
                                if (t == CardType.Alchemy || t == CardType.Comet || !HasSpell(seat, t)) continue;
                                if (t == CardType.Ignite)
                                {
                                    foreach (var tgt in Players.Where(p => p.Seat != seat && p.Alive
                                        && p.Hand.Any(c => c.Type != CardType.Hero && !IsMarked(c))))
                                        list.Add(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = card.Id,
                                            TargetType = t, TargetSeat = tgt.Seat });
                                }
                                else if (t == CardType.Thunder)
                                {
                                    foreach (var tgt in Players.Where(p => p.Seat != seat && p.Alive && p.Hand.Count > 0))
                                        list.Add(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = card.Id,
                                            TargetType = t, TargetSeat = tgt.Seat });
                                }
                                else
                                {
                                    list.Add(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = card.Id, TargetType = t });
                                }
                            }
                            break;
                        case CardType.Clarity:
                            if (SideDeck.Count > 0)   // 牌库空了放着只是浪费牌
                                list.Add(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = card.Id, TargetType = card.Type });   // 无目标
                            break;
                        case CardType.Ignite:
                            // 2026-10-21② 用户强化：行动只选目标对手；释放时亮其2张手牌再选1张（见 ResolveSpell）
                            foreach (var t in Players.Where(p => p.Seat != seat && p.Alive
                                && p.Hand.Any(c => c.Type != CardType.Hero && !IsMarked(c))))
                                list.Add(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = card.Id,
                                    TargetType = card.Type, TargetSeat = t.Seat });
                            break;
                        case CardType.Thunder:
                            foreach (var t in Players.Where(p => p.Seat != seat && p.Alive && p.Hand.Count > 0))
                                list.Add(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = card.Id, TargetType = card.Type, TargetSeat = t.Seat });
                            break;
                        case CardType.Barrier:
                            list.Add(new GameAction { Kind = ActionKind.CastSpell, SourceCardId = card.Id, TargetType = card.Type });   // 无目标
                            break;
                    }
                }

            // 攻击被标记的牌（清晰术明置目标）
            foreach (var p in Players.Where(p => p.Seat != seat && p.Alive))
                foreach (var marked in MarkedCards(p.Seat))
                    list.Add(new GameAction { Kind = ActionKind.Attack, TargetSeat = p.Seat, SecondCardId = marked.Id });

            return list;
        }
    }
}

