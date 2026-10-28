using System;
using System.Collections.Generic;
using System.Linq;

namespace SandKill.Core
{
    /// <summary>
    /// 2代规则引擎：英雄HP/MP系统（删除工人）、法力值限制技能、
    /// 每2回合免费抽1张牌（牌堆耗尽即止，不洗牌）、天赋"每2回合回1点HP"、
    /// 新牌：带刀侍卫/大臣/军机大臣。
    /// 与初代引擎 GameEngine 完全独立，互不影响。
    ///
    /// 核心规则：
    /// - 英雄牌永不离开手牌：攻击抽中/警察扣押/勒索打中英雄牌，一律改为扣1点HP
    /// - HP归零出局；牧师亡语/女巫解药不能回收英雄牌，牧师亡语不能回收牧师
    /// - 普攻免费，每回合1次；主动技能耗蓝（强技能2点：毒药/勒索；其余1点）
    /// - 贿赂结盟：到贿赂者的下回合开始前，被贿赂者不能攻击贿赂者（含军火库追攻/毒药）
    /// </summary>
    public class GameEngine2
    {
        // ---- 蓝耗（普攻免费）----
        public const int COST_EXORCISE = 1, COST_SEIZE = 1, COST_ANTIDOTE = 1,
                         COST_ARSENAL = 1, COST_BRIBE = 1, COST_ARMGUARD = 1, COST_HIDE = 1,
                         COST_POISON = 2, COST_EXTORT = 2;

        /// <summary>英雄模板。未来加新英雄 = 在这张表里加一行 + 在ApplyAction的HeroPower分支里实现。</summary>
        public class HeroSpec
        {
            public int Id; public string Name; public int HP; public int MP;
            public bool HealTalent;   // 医师：每2回合回1点HP
            public bool HasPower;     // 有主动技（整局限1次）
            public string Desc;
        }
        public static readonly HeroSpec[] HeroRoster =
        {
            new HeroSpec { Id = 0, Name = "医师", HP = 3, MP = 3, HealTalent = true,
                Desc = "天赋：每2回合回复1点HP" },
            new HeroSpec { Id = 1, Name = "police station", HP = 3, MP = 3, HasPower = true,
                Desc = "主动技（限1次）：所有敌人的下个回合无法使用任何技能（含被动）" },
            new HeroSpec { Id = 2, Name = "save", HP = 3, MP = 3, HasPower = true,
                Desc = "主动技（限1次）：本回合技能不限使用次数（蓝耗照付）" },
        };

        // ---- 勒索可选的牌型 ----
        public static readonly CardType[] ExtortTypes =
        {
            CardType.Undead, CardType.Exorcist, CardType.Priest, CardType.Police,
            CardType.Guard, CardType.Witch, CardType.Arsenal, CardType.ArmedGuard,
            CardType.Minister, CardType.GrandMinister, CardType.Hero
        };

        public List<PlayerState> Players;
        public List<Card> Discard = new List<Card>();
        public List<Card> SideDeck = new List<Card>();   // 未发完的牌堆（耗尽即止）
        public int Current;
        public PendingChoice Pending;
        public bool GameOver;
        public int WinnerSeat = -1;
        public bool BasicAttackUsed;
        public int TurnNumber = 1;
        public List<IPlayerBrain2> Brains;
        public List<string> History = new List<string>();
        public event Action<string> OnLog;
        /// <summary>公开敌意信息（攻击者, 受害者, 类型），供AI记忆。勒索对每个受害玩家各发一次。</summary>
        public event Action<int, int, ActionKind> OnHostile;

        // ---- 英雄模板（未来做新英雄时改这里/做成配置）----
        public int HeroHP = 3, HeroMP = 3, HealEvery = 2, DrawEvery = 2;

        class HeldCard { public Card Card; public int OwnerSeat; public int HolderSeat; public int TurnsLeft; }
        readonly List<HeldCard> _held = new List<HeldCard>();
        readonly HashSet<int> _armGuard = new HashSet<int>();  // 带刀侍卫buff生效中的座位
        readonly List<int[]> _alliances = new List<int[]>();   // [被贿赂者, 贿赂者]
        readonly HashSet<int> _silenced = new HashSet<int>();  // police station：下个回合技能全封（含被动）
        readonly HashSet<int> _freeSkills = new HashSet<int>(); // save：本回合技能免费不限次
        readonly Dictionary<int, List<Card>> _hidden = new Dictionary<int, List<Card>>(); // 守卫藏起的牌
        HeroSpec[] _heroes;
        bool[] _powerUsed;
        readonly Random _rng;

        public GameEngine2(Dictionary<CardType, int> deckCounts, string[] names, List<IPlayerBrain2> brains, int seed)
        {
            _rng = new Random(seed);
            Brains = brains;
            Players = new List<PlayerState>();
            _heroes = new HeroSpec[names.Length];
            _powerUsed = new bool[names.Length];
            var heroPool = HeroRoster.OrderBy(_ => _rng.Next()).ToList();   // 开局随机抽英雄，互不重复
            for (int i = 0; i < names.Length; i++)
            {
                var spec = heroPool[i % heroPool.Count];
                _heroes[i] = spec;
                Players.Add(new PlayerState
                {
                    Seat = i, Name = names[i], IsHuman = brains[i].IsHuman,
                    MaxHP = spec.HP, HP = spec.HP, MaxMP = spec.MP, MP = spec.MP,
                    HeroId = spec.Id, HeroName = spec.Name
                });
            }

            var deck = new List<Card>();
            int id = 0;
            foreach (var kv in deckCounts)
                for (int i = 0; i < kv.Value; i++) deck.Add(new Card(id++, kv.Key));
            for (int i = deck.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); var t = deck[i]; deck[i] = deck[j]; deck[j] = t; }

            int cursor = 0;
            foreach (var p in Players)
            {
                for (int k = 0; k < 9 && cursor < deck.Count; k++) p.Hand.Add(deck[cursor++]);
                p.Hand.Add(new Card(9000 + p.Seat, CardType.Hero));   // 英雄牌不属于牌库
            }
            SideDeck.AddRange(deck.Skip(cursor));
            Log("对局开始：牌库" + deck.Count + "张，每人9张+英雄牌，剩余" + SideDeck.Count + "张作牌堆。");
            foreach (var p in Players)
                Log(p.Name + " 抽到了英雄【" + p.HeroName + "】（" + _heroes[p.Seat].Desc + "）。");
            StartTurn(0);
        }

        void Log(string msg) { History.Add(msg); if (OnLog != null) OnLog(msg); }
        void RaiseHostile(int attacker, int victim, ActionKind kind) { if (OnHostile != null) OnHostile(attacker, victim, kind); }

        /// <summary>卡牌守恒（30张牌库+每人1张英雄牌）。</summary>
        public int TotalCards { get { return Players.Sum(p => p.Hand.Count) + Discard.Count + SideDeck.Count + _held.Count + _hidden.Values.Sum(l => l.Count); } }

        /// <summary>结盟保护：attackerSeat 是否被禁止攻击 victimSeat。</summary>
        public bool IsProtected(int attackerSeat, int victimSeat)
        {
            return _alliances.Any(a => a[0] == attackerSeat && a[1] == victimSeat);
        }

        public bool HasArmGuard(int seat) { return _armGuard.Contains(seat); }

        public bool IsSilenced(int seat) { return _silenced.Contains(seat); }
        public int HiddenCount(int seat) { return _hidden.TryGetValue(seat, out var l) ? l.Count : 0; }
        public HeroSpec HeroOf(int seat) { return _heroes[seat]; }

        /// <summary>seat 贿赂了谁（结盟对象座位，无则-1），UI显示用。</summary>
        public int BribedBy(int briberSeat)
        {
            var a = _alliances.FirstOrDefault(x => x[1] == briberSeat);
            return a == null ? -1 : a[0];
        }

        void StartTurn(int seat)
        {
            var p = Players[seat];
            p.OwnTurnCount++;
            p.MP = p.MaxMP;
            _armGuard.Remove(seat);                          // 自己的带刀侍卫buff到自己的下回合开始过期
            _alliances.RemoveAll(a => a[1] == seat);         // 我贿赂来的保护到我的下回合开始过期
            if (_hidden.TryGetValue(seat, out var hid) && hid.Count > 0)
            {
                foreach (var c in hid) p.Hand.Add(c);
                Log(p.Name + " 藏起的" + hid.Count + "张牌回到了手中。");
                hid.Clear();
            }
            if (_heroes[seat].HealTalent && p.OwnTurnCount % HealEvery == 0 && p.HP < p.MaxHP)
            {
                p.HP++;
                Log(p.Name + " 的天赋生效：回复1点HP（" + p.HP + "/" + p.MaxHP + "）。");
            }
            if (p.OwnTurnCount % DrawEvery == 0 && SideDeck.Count > 0)
            {
                var c = SideDeck[0]; SideDeck.RemoveAt(0); p.Hand.Add(c);
                Log(p.Name + " 从牌堆补充了1张牌。（牌堆剩余" + SideDeck.Count + "）");
            }
            BasicAttackUsed = false;
            foreach (var c in p.Hand) c.UsedSkillThisTurn = false;
        }

        // ------------------------------------------------------------------
        // 合法行动
        // ------------------------------------------------------------------

        public List<GameAction> GetLegalActions(int seat)
        {
            var list = new List<GameAction>();
            if (Pending != null || GameOver || seat != Current || !Players[seat].Alive) return list;
            var me = Players[seat];
            var targets = Players.Where(p => p.Alive && p.Seat != seat && p.Hand.Count > 0).ToList();
            bool silenced = _silenced.Contains(seat);
            bool free = _freeSkills.Contains(seat);

            if (!BasicAttackUsed)
                foreach (var t in targets)
                    if (!IsProtected(seat, t.Seat))
                        list.Add(new GameAction { Kind = ActionKind.Attack, TargetSeat = t.Seat });

            if (!silenced)
            foreach (var c in me.Hand)
            {
                if (c.Type == CardType.Hero || (c.UsedSkillThisTurn && !free)) continue;
                switch (c.Type)
                {
                    case CardType.Exorcist:
                        if (me.MP >= COST_EXORCISE)
                            foreach (var t in targets)
                                list.Add(new GameAction { Kind = ActionKind.Exorcise, TargetSeat = t.Seat, SourceCardId = c.Id });
                        break;
                    case CardType.Police:
                        if (me.MP >= COST_SEIZE)
                            foreach (var t in targets)
                                list.Add(new GameAction { Kind = ActionKind.Seize, TargetSeat = t.Seat, SourceCardId = c.Id });
                        break;
                    case CardType.Witch:
                        if (!c.PoisonUsed && me.MP >= COST_POISON)
                            foreach (var t in targets)
                                if (!IsProtected(seat, t.Seat))
                                    list.Add(new GameAction { Kind = ActionKind.PoisonAttack, TargetSeat = t.Seat, SourceCardId = c.Id });
                        if (!c.AntidoteUsed && me.MP >= COST_ANTIDOTE && Discard.Any(d => d.Type.IsRole() && d.Type != CardType.Hero))
                            list.Add(new GameAction { Kind = ActionKind.Antidote, SourceCardId = c.Id });
                        break;
                    case CardType.Arsenal:
                        if (me.MP >= COST_ARSENAL)
                            foreach (var t in targets)
                                if (!IsProtected(seat, t.Seat))
                                    list.Add(new GameAction { Kind = ActionKind.ArsenalAttack, TargetSeat = t.Seat, SourceCardId = c.Id });
                        break;
                    case CardType.Minister:
                        if (me.MP >= COST_BRIBE)
                        {
                            var giveable = me.Hand.Where(x => x.Type != CardType.Hero && x.Id != c.Id).ToList();
                            foreach (var t in Players.Where(p => p.Alive && p.Seat != seat))
                            {
                                if (IsProtected(t.Seat, seat)) continue;   // 结盟生效中不能重复贿赂（防止互相续命的死循环）
                                foreach (var g in giveable)
                                    list.Add(new GameAction { Kind = ActionKind.Bribe, TargetSeat = t.Seat, SourceCardId = c.Id, SecondCardId = g.Id });
                            }
                        }
                        break;
                    case CardType.GrandMinister:
                        if (me.MP >= COST_EXTORT)
                            foreach (var tt in ExtortTypes)
                                list.Add(new GameAction { Kind = ActionKind.Extort, SourceCardId = c.Id, TargetType = tt });
                        break;
                    case CardType.ArmedGuard:
                        if (me.MP >= COST_ARMGUARD && !_armGuard.Contains(seat))
                            list.Add(new GameAction { Kind = ActionKind.ArmGuard, SourceCardId = c.Id });
                        break;
                    case CardType.Warden:
                        if (me.MP >= COST_HIDE)
                            foreach (var h in me.Hand)
                            {
                                if (h.Type == CardType.Hero || h.Id == c.Id) continue;   // 不能藏英雄/藏自己
                                if (h.LastHiddenTurn == me.OwnTurnCount - 1) continue;    // 不能连续两回合藏同一张
                                list.Add(new GameAction { Kind = ActionKind.Hide, SourceCardId = c.Id, SecondCardId = h.Id });
                            }
                        break;
                }
            }
            if (!silenced && _heroes[seat].HasPower && !_powerUsed[seat])
                list.Add(new GameAction { Kind = ActionKind.HeroPower });
            list.Add(new GameAction { Kind = ActionKind.EndTurn });
            return list;
        }

        // ------------------------------------------------------------------
        // 行动结算
        // ------------------------------------------------------------------

        public void ApplyAction(GameAction a)
        {
            ApplyActionCore(a);
            EndTurnIfCurrentDead();
        }

        /// <summary>当前玩家可能在自己的回合内被亡语反杀：死了就立即结束回合，防止"死人当值"卡死。</summary>
        void EndTurnIfCurrentDead()
        {
            if (!GameOver && Pending == null && !Players[Current].Alive) EndTurn();
        }

        void ApplyActionCore(GameAction a)
        {
            if (GameOver || Pending != null) return;
            var me = Players[Current];
            if (!me.Alive) return;
            int seat = Current;
            bool free = _freeSkills.Contains(seat);
            if (_silenced.Contains(seat) && a.Kind != ActionKind.Attack && a.Kind != ActionKind.EndTurn) return;

            switch (a.Kind)
            {
                case ActionKind.Attack:
                    if (BasicAttackUsed || !ValidTarget(seat, a.TargetSeat) || IsProtected(seat, a.TargetSeat)) return;
                    BasicAttackUsed = true;
                    DoAttack(seat, a.TargetSeat, a.Kind);
                    break;
                case ActionKind.ArsenalAttack:
                case ActionKind.PoisonAttack:
                    {
                        if (!ValidTarget(seat, a.TargetSeat) || IsProtected(seat, a.TargetSeat)) return;
                        int cost = a.Kind == ActionKind.PoisonAttack ? COST_POISON : COST_ARSENAL;
                        CardType expect = a.Kind == ActionKind.PoisonAttack ? CardType.Witch : CardType.Arsenal;
                        var probe = me.Hand.FirstOrDefault(c => c.Id == a.SourceCardId && c.Type == expect);
                        if (probe == null || (a.Kind == ActionKind.PoisonAttack && probe.PoisonUsed)) return;
                        var card = PaySkill(me, a.SourceCardId, cost, expect, free);
                        if (card == null) return;
                        if (a.Kind == ActionKind.PoisonAttack) card.PoisonUsed = true;
                        DoAttack(seat, a.TargetSeat, a.Kind);
                        break;
                    }
                case ActionKind.Exorcise:
                    {
                        if (!ValidTarget(seat, a.TargetSeat)) return;
                        var card = PaySkill(me, a.SourceCardId, COST_EXORCISE, CardType.Exorcist, free);
                        if (card == null) return;
                        RaiseHostile(seat, a.TargetSeat, a.Kind);
                        ResolveExorcise(seat, a.TargetSeat);
                        break;
                    }
                case ActionKind.Seize:
                    {
                        if (!ValidTarget(seat, a.TargetSeat)) return;
                        var card = PaySkill(me, a.SourceCardId, COST_SEIZE, CardType.Police, free);
                        if (card == null) return;
                        RaiseHostile(seat, a.TargetSeat, a.Kind);
                        ResolveSeize(seat, a.TargetSeat);
                        break;
                    }
                case ActionKind.Antidote:
                    {
                        var card = PaySkill(me, a.SourceCardId, COST_ANTIDOTE, CardType.Witch, free);
                        if (card == null || card.AntidoteUsed) return;
                        var options = DiscardOptions(includePriest: true);
                        if (options.Count == 0) return;
                        card.AntidoteUsed = true;
                        Log(me.Name + " 使用女巫·解药：从弃牌堆回收一张角色牌。");
                        OfferDiscardPick(me, options, "女巫·解药");
                        break;
                    }
                case ActionKind.Bribe:
                    {
                        var card = PaySkill(me, a.SourceCardId, COST_BRIBE, CardType.Minister, free);
                        if (card == null) return;
                        ResolveBribe(seat, a.TargetSeat, a.SecondCardId);
                        break;
                    }
                case ActionKind.Extort:
                    {
                        var card = PaySkill(me, a.SourceCardId, COST_EXTORT, CardType.GrandMinister, free);
                        if (card == null) return;
                        ResolveExtort(seat, a.TargetType);
                        break;
                    }
                case ActionKind.ArmGuard:
                    {
                        var card = PaySkill(me, a.SourceCardId, COST_ARMGUARD, CardType.ArmedGuard, free);
                        if (card == null || _armGuard.Contains(seat)) return;
                        _armGuard.Add(seat);
                        Log(me.Name + " 发动【带刀侍卫】：1回合内，自己的侍卫和大臣获得亡语“攻击1次”。");
                        break;
                    }
                case ActionKind.Hide:
                    {
                        var card = PaySkill(me, a.SourceCardId, COST_HIDE, CardType.Warden, free);
                        if (card == null) return;
                        var hide = me.Hand.FirstOrDefault(c => c.Id == a.SecondCardId && c.Type != CardType.Hero && c.Id != card.Id);
                        if (hide == null || hide.LastHiddenTurn == me.OwnTurnCount - 1) return;
                        me.Hand.Remove(hide);
                        if (!_hidden.TryGetValue(seat, out var hl)) { hl = new List<Card>(); _hidden[seat] = hl; }
                        hl.Add(hide);
                        hide.LastHiddenTurn = me.OwnTurnCount;
                        Log(me.Name + " 用【守卫】藏起了1张牌（自己的下回合开始时拿回）。");
                        break;
                    }
                case ActionKind.HeroPower:
                    {
                        var spec = _heroes[seat];
                        if (!spec.HasPower || _powerUsed[seat]) return;
                        _powerUsed[seat] = true;
                        if (spec.Id == 1)
                        {
                            foreach (var o in Players.Where(p => p.Alive && p.Seat != seat))
                            {
                                _silenced.Add(o.Seat);
                                RaiseHostile(seat, o.Seat, ActionKind.HeroPower);
                            }
                            Log(me.Name + " 发动英雄技【police station】：所有敌人的下个回合什么技能都用不了（含被动）！");
                        }
                        else if (spec.Id == 2)
                        {
                            _freeSkills.Add(seat);
                            Log(me.Name + " 发动英雄技【save】：本回合技能不限使用次数（蓝耗照付）！");
                        }
                        break;
                    }
                case ActionKind.EndTurn:
                    EndTurn();
                    break;
            }
        }

        Card PaySkill(PlayerState me, int cardId, int cost, CardType expect, bool free = false)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id == cardId && c.Type == expect);
            if (card == null) return null;
            if (!free && card.UsedSkillThisTurn) return null;   // save期间才允许同卡连发
            if (me.MP < cost) return null;                       // 蓝耗照付
            me.MP -= cost;
            card.UsedSkillThisTurn = true;
            return card;
        }

        bool ValidTarget(int attackerSeat, int targetSeat)
        {
            if (targetSeat < 0 || targetSeat >= Players.Count || targetSeat == attackerSeat) return false;
            var t = Players[targetSeat];
            return t.Alive && t.Hand.Count > 0;
        }

        void DoAttack(int attackerSeat, int victimSeat, ActionKind kind)
        {
            RaiseHostile(attackerSeat, victimSeat, kind);
            string tag = kind == ActionKind.PoisonAttack ? "（毒药）" : kind == ActionKind.ArsenalAttack ? "（军火库）" : "";
            Log(Players[attackerSeat].Name + " 攻击 " + Players[victimSeat].Name + tag + "！");
            ResolveAttack(attackerSeat, victimSeat, kind);
        }

        // ------------------------------------------------------------------
        // 各种结算
        // ------------------------------------------------------------------

        void ResolveAttack(int attackerSeat, int victimSeat, ActionKind kind)
        {
            var v = Players[victimSeat];
            if (!v.Alive || v.Hand.Count == 0) return;
            var drawn = v.Hand[_rng.Next(v.Hand.Count)];
            // 被沉默者：侍卫不能挡刀，一切被动失效
            var guard = _silenced.Contains(victimSeat) ? null : v.Hand.FirstOrDefault(c => c.Type == CardType.Guard);
            if (guard != null && drawn.Type != CardType.Guard)
            {
                if (v.IsHuman)
                {
                    Pending = new PendingChoice { Kind = ChoiceKind.GuardIntercept, Seat = victimSeat, DrawnCard = drawn, AttackerSeat = attackerSeat, AttackKind = kind };
                    return;
                }
                if (Brains[victimSeat].ChooseGuardIntercept(this, victimSeat, drawn, attackerSeat))
                {
                    DiscardGuard(v, guard, attackerSeat);
                    return;
                }
            }
            ResolveDrawn(attackerSeat, victimSeat, drawn, kind);
        }

        void ResolveDrawn(int attackerSeat, int victimSeat, Card drawn, ActionKind kind)
        {
            var v = Players[victimSeat];
            bool victimSilenced = _silenced.Contains(victimSeat);
            if (drawn.Type == CardType.Hero)
            {
                v.HP--;
                Log(v.Name + " 的英雄牌被抽中！损失1点HP（" + Math.Max(v.HP, 0) + "/" + v.MaxHP + "），英雄牌回到手中。");
                CheckElimination(v);
                return;
            }
            if (drawn.Type == CardType.Undead && !victimSilenced)
            {
                Log(v.Name + " 的【亡灵】免疫攻击，回到手中。");
                return;
            }
            if (drawn.Type == CardType.Maid && kind == ActionKind.ArsenalAttack && !victimSilenced)
            {
                Log(v.Name + " 的【侍女】免疫了军火库的攻击，回到手中。");
                return;
            }
            v.Hand.Remove(drawn);
            Discard.Add(drawn);
            Log(Players[attackerSeat].Name + " 抽走了 " + v.Name + " 的【" + drawn.Type.Name() + "】，进入弃牌堆。");
            if (victimSilenced)
            {
                // 被沉默：亡语不触发
            }
            else if (drawn.Type == CardType.Priest)
            {
                // 牧师亡语：不能回收英雄牌，也不能回收牧师（杜绝套娃）
                var options = DiscardOptions(includePriest: false).Where(c => c.Id != drawn.Id).ToList();
                if (options.Count == 0) Log("牧师亡语触发，但弃牌堆没有可回收的角色牌。");
                else
                {
                    Log(v.Name + " 的牧师亡语触发。");
                    OfferDiscardPick(v, options, "牧师亡语");
                }
            }
            else if ((drawn.Type == CardType.Guard || drawn.Type == CardType.Minister) && HasArmGuard(v.Seat))
            {
                DeathrattleAttack(v.Seat);
            }
        }

        void DiscardGuard(PlayerState v, Card guard, int attackerSeat)
        {
            v.Hand.Remove(guard);
            Discard.Add(guard);
            Log(v.Name + " 的【侍卫】挺身而出，代替进入弃牌堆！");
            if (HasArmGuard(v.Seat) && !_silenced.Contains(v.Seat)) DeathrattleAttack(v.Seat);
        }

        /// <summary>带刀侍卫亡语：立即自由攻击1次（人类弹Pending选目标，AI同步决策）。</summary>
        void DeathrattleAttack(int ownerSeat)
        {
            var o = Players[ownerSeat];
            if (!o.Alive || GameOver) return;
            var candidates = Players.Where(p => p.Alive && p.Seat != ownerSeat && p.Hand.Count > 0
                                                && !IsProtected(ownerSeat, p.Seat))
                                    .Select(p => p.Seat).ToList();
            if (candidates.Count == 0) { Log(o.Name + " 的亡语攻击没有可打的目标。"); return; }
            Log(o.Name + " 的带刀侍卫亡语触发：立即攻击1次！");
            if (o.IsHuman)
            {
                Pending = new PendingChoice { Kind = ChoiceKind.DeathrattleAttack, Seat = ownerSeat };
            }
            else
            {
                int t = Brains[ownerSeat].ChooseDeathrattleTarget(this, ownerSeat, candidates);
                if (candidates.Contains(t))
                {
                    RaiseHostile(ownerSeat, t, ActionKind.Attack);
                    Log(o.Name + " 的亡语攻击打向 " + Players[t].Name + "！");
                    ResolveAttack(ownerSeat, t, ActionKind.Attack);
                }
            }
        }

        void ResolveExorcise(int exorcistSeat, int targetSeat)
        {
            var t = Players[targetSeat];
            if (t.Hand.Count == 0) return;
            var c = t.Hand[_rng.Next(t.Hand.Count)];
            if (c.Type == CardType.Undead)
            {
                t.Hand.Remove(c);
                Discard.Add(c);
                Log(Players[exorcistSeat].Name + " 的驱魔师当场处决了 " + t.Name + " 的【亡灵】！");
            }
            else
            {
                Log("驱魔抽中【" + c.Type.Name() + "】，不是亡灵，放回去了。（驱魔方已获知该情报）");
            }
        }

        void ResolveSeize(int seizerSeat, int targetSeat)
        {
            var t = Players[targetSeat];
            if (t.Hand.Count == 0) return;
            var c = t.Hand[_rng.Next(t.Hand.Count)];
            if (c.Type == CardType.Hero)
            {
                t.HP--;
                Log(Players[seizerSeat].Name + " 的扣押打中了 " + t.Name + " 的英雄牌：改为损失1点HP（" + Math.Max(t.HP, 0) + "/" + t.MaxHP + "）！");
                CheckElimination(t);
                return;
            }
            t.Hand.Remove(c);
            _held.Add(new HeldCard { Card = c, OwnerSeat = targetSeat, HolderSeat = seizerSeat, TurnsLeft = 1 });
            Log(Players[seizerSeat].Name + " 扣押了 " + t.Name + " 的一张牌（" + t.Name + " 的下个回合结束时归还）。");
        }

        void ResolveBribe(int briberSeat, int targetSeat, int giveCardId)
        {
            var me = Players[briberSeat];
            if (targetSeat < 0 || targetSeat >= Players.Count || targetSeat == briberSeat || !Players[targetSeat].Alive) return;
            var give = me.Hand.FirstOrDefault(c => c.Id == giveCardId && c.Type != CardType.Hero);
            if (give == null) return;
            me.Hand.Remove(give);
            Players[targetSeat].Hand.Add(give);
            _alliances.Add(new[] { targetSeat, briberSeat });
            Log(me.Name + " 把【" + give.Type.Name() + "】交给 " + Players[targetSeat].Name + " 作为贿赂：结盟生效，到 "
                + me.Name + " 的下回合开始前，" + Players[targetSeat].Name + " 不能攻击 " + me.Name + "。");
        }

        void ResolveExtort(int extorterSeat, CardType type)
        {
            var me = Players[extorterSeat];
            Log(me.Name + " 发动【军机大臣·勒索】，指定牌型【" + type.Name() + "】！");
            foreach (var o in Players.Where(p => p.Alive && p.Seat != extorterSeat).ToList())
            {
                if (GameOver) break;
                if (type == CardType.Hero)
                {
                    o.HP--;
                    RaiseHostile(extorterSeat, o.Seat, ActionKind.Extort);
                    Log(o.Name + " 的英雄牌被勒索打中：损失1点HP（" + Math.Max(o.HP, 0) + "/" + o.MaxHP + "）！");
                    CheckElimination(o);
                }
                else
                {
                    var taken = o.Hand.Where(c => c.Type == type).ToList();
                    foreach (var c in taken) { o.Hand.Remove(c); me.Hand.Add(c); }
                    if (taken.Count > 0)
                    {
                        RaiseHostile(extorterSeat, o.Seat, ActionKind.Extort);
                        Log("勒索了 " + o.Name + " 的 " + taken.Count + " 张【" + type.Name() + "】。");
                    }
                    else Log(o.Name + " 没有【" + type.Name() + "】。");
                }
            }
        }

        /// <summary>弃牌堆可回收的角色牌（每种类型留一张）。牧师亡语不含牧师。</summary>
        List<Card> DiscardOptions(bool includePriest)
        {
            return Discard.Where(c => c.Type.IsRole() && c.Type != CardType.Hero && (includePriest || c.Type != CardType.Priest))
                          .GroupBy(c => c.Type).Select(g => g.First()).ToList();
        }

        void OfferDiscardPick(PlayerState owner, List<Card> options, string reason)
        {
            if (!owner.Alive) return;
            if (owner.IsHuman)
            {
                Pending = new PendingChoice { Kind = ChoiceKind.PickFromDiscard, Seat = owner.Seat, Options = options, Reason = reason };
            }
            else
            {
                int pickId = Brains[owner.Seat].ChooseFromDiscard(this, owner.Seat, options);
                var card = options.FirstOrDefault(c => c.Id == pickId) ?? options[0];
                Discard.Remove(card);
                owner.Hand.Add(card);
                Log(owner.Name + " 从弃牌堆回收了【" + card.Type.Name() + "】。");
            }
        }

        // ------------------------------------------------------------------
        // 人类玩家的Pending提交
        // ------------------------------------------------------------------

        public void SubmitGuardChoice(bool intercept)
        {
            var pc = Pending;
            if (pc == null || pc.Kind != ChoiceKind.GuardIntercept) return;
            Pending = null;
            var v = Players[pc.Seat];
            if (intercept)
            {
                var guard = v.Hand.FirstOrDefault(c => c.Type == CardType.Guard);
                if (guard != null) { DiscardGuard(v, guard, pc.AttackerSeat); return; }
            }
            Log(v.Name + " 选择保留【侍卫】，不挡刀。");
            ResolveDrawn(pc.AttackerSeat, pc.Seat, pc.DrawnCard, pc.AttackKind);
            EndTurnIfCurrentDead();
        }

        public void SubmitDiscardPick(int cardId)
        {
            var pc = Pending;
            if (pc == null || pc.Kind != ChoiceKind.PickFromDiscard) return;
            var card = pc.Options.FirstOrDefault(c => c.Id == cardId);
            if (card == null) return;
            Pending = null;
            Discard.Remove(card);
            Players[pc.Seat].Hand.Add(card);
            Log(Players[pc.Seat].Name + " 从弃牌堆回收了【" + card.Type.Name() + "】。");
            EndTurnIfCurrentDead();
        }

        public void SubmitDeathrattleTarget(int targetSeat)
        {
            var pc = Pending;
            if (pc == null || pc.Kind != ChoiceKind.DeathrattleAttack) return;
            if (!Players[pc.Seat].Alive) { Pending = null; return; }
            if (targetSeat < 0 || targetSeat >= Players.Count || targetSeat == pc.Seat
                || !Players[targetSeat].Alive || Players[targetSeat].Hand.Count == 0
                || IsProtected(pc.Seat, targetSeat)) return;
            Pending = null;
            RaiseHostile(pc.Seat, targetSeat, ActionKind.Attack);
            Log(Players[pc.Seat].Name + " 的亡语攻击打向 " + Players[targetSeat].Name + "！");
            ResolveAttack(pc.Seat, targetSeat, ActionKind.Attack);
            EndTurnIfCurrentDead();
        }

        // ------------------------------------------------------------------
        // 回合结束 / 淘汰
        // ------------------------------------------------------------------

        void EndTurn()
        {
            _freeSkills.Remove(Current);   // save只持续本回合
            _silenced.Remove(Current);     // police station的沉默到被沉默者的回合结束解除
            // 归还扣押牌（持有者的回合结束时倒计时）
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                var h = _held[i];
                if (h.HolderSeat != Current) continue;
                h.TurnsLeft--;
                if (h.TurnsLeft <= 0)
                {
                    _held.RemoveAt(i);
                    if (Players[h.OwnerSeat].Alive)
                    {
                        Players[h.OwnerSeat].Hand.Add(h.Card);
                        Log("被扣押的牌已归还手牌：" + Players[h.OwnerSeat].Name);
                    }
                    else Discard.Add(h.Card);
                }
            }
            int n = Players.Count, old = Current, seat = Current;
            do { seat = (seat + 1) % n; } while (!Players[seat].Alive && seat != Current);
            Current = seat;
            if (seat < old) TurnNumber++;
            StartTurn(Current);
        }

        void CheckElimination(PlayerState p)
        {
            if (!p.Alive || p.HP > 0) return;
            p.Alive = false;
            Log(p.Name + " HP归零，出局！");
            foreach (var c in p.Hand.ToList()) { p.Hand.Remove(c); Discard.Add(c); }
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                var h = _held[i];
                if (h.OwnerSeat == p.Seat) { Discard.Add(h.Card); _held.RemoveAt(i); }
                else if (h.HolderSeat == p.Seat)
                {
                    _held.RemoveAt(i);
                    if (Players[h.OwnerSeat].Alive) Players[h.OwnerSeat].Hand.Add(h.Card);
                    else Discard.Add(h.Card);
                }
            }
            var alive = Players.Where(x => x.Alive).ToList();
            if (alive.Count == 1)
            {
                GameOver = true;
                WinnerSeat = alive[0].Seat;
                Log(alive[0].Name + " 获得胜利！");
            }
        }
    }
}
