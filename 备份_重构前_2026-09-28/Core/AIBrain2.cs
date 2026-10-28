using System;
using System.Collections.Generic;
using System.Linq;

namespace SandKill.Core
{
    /// <summary>
    /// 真人占位（2代）：真人由UI驱动，引擎不会调用真人大脑的方法。
    /// </summary>
    public class HumanBrain2 : IPlayerBrain2
    {
        public bool IsHuman { get { return true; } }
        public GameAction ChooseAction(GameEngine2 g, int seat, List<GameAction> legal) { throw new InvalidOperationException("真人由UI驱动"); }
        public bool ChooseGuardIntercept(GameEngine2 g, int seat, Card drawn, int attackerSeat) { throw new InvalidOperationException("真人由UI驱动"); }
        public int ChooseFromDiscard(GameEngine2 g, int seat, List<Card> options) { throw new InvalidOperationException("真人由UI驱动"); }
        public int ChooseDeathrattleTarget(GameEngine2 g, int seat, List<int> candidates) { throw new InvalidOperationException("真人由UI驱动"); }
    }

    /// <summary>
    /// 2代AI大脑：继承初代v3.1的全部性格（记仇/军火库记忆/自认最强才斩杀/1v1火力全开/绝望阎王），
    /// 新增：蓝耗规划、带刀侍卫布防、勒索收益判断（含"勒索英雄"斩杀）、贿赂自保。
    /// </summary>
    public class AIBrain2 : IPlayerBrain2
    {
        public bool IsHuman { get { return false; } }

        readonly Random _rng;
        readonly int _seat;
        readonly float[] _grudge;        // 记仇值：谁打过我我记谁
        readonly bool[] _arsenalSeen;    // 军火库记忆：谁用过军火库
        readonly Dictionary<int, int> _usedCount = new Dictionary<int, int>(); // 本回合每张技能牌已用次数
        int _lastOwnTurn = -1;
        const int MAX_USE_PER_CARD = 2;  // save免费期间也最多连发2次：防止无限驱魔表演

        public AIBrain2(int seed, int seat, int playerCount)
        {
            _rng = new Random(seed);
            _seat = seat;
            _grudge = new float[playerCount];
            _arsenalSeen = new bool[playerCount];
        }

        /// <summary>订阅引擎 OnHostile：AI只记公开信息，不偷看手牌。</summary>
        public void NoteHostile(int attacker, int victim, ActionKind kind)
        {
            if (kind == ActionKind.ArsenalAttack) _arsenalSeen[attacker] = true;
            if (victim == _seat)
            {
                _grudge[attacker] += kind == ActionKind.PoisonAttack ? 2f : 1f;
            }
        }

        public float GrudgeOf(int seat) { return _grudge[seat]; }
        public bool KnowsArsenal(int seat) { return _arsenalSeen[seat]; }

        // ------------------------------------------------------------------
        // 主决策
        // ------------------------------------------------------------------

        public GameAction ChooseAction(GameEngine2 g, int seat, List<GameAction> legal)
        {
            var me = g.Players[seat];
            if (me.OwnTurnCount != _lastOwnTurn) { _usedCount.Clear(); _lastOwnTurn = me.OwnTurnCount; }
            legal = legal.Where(a => a.SourceCardId < 0 || !_usedCount.TryGetValue(a.SourceCardId, out var n) || n < MAX_USE_PER_CARD).ToList();
            var act = ChooseActionCore(g, seat, legal, me);
            if (act.SourceCardId >= 0) _usedCount[act.SourceCardId] = _usedCount.TryGetValue(act.SourceCardId, out var m) ? m + 1 : 1;
            return act;
        }

        GameAction ChooseActionCore(GameEngine2 g, int seat, List<GameAction> legal, PlayerState me)
        {
            int aliveOpponents = g.Players.Count(x => x.Alive && x.Seat != seat);

            // 0. 英雄技：police station优势压制/绝境反打（要在攻击前放，封住对面被动）；save技能多时才赚
            var power = legal.FirstOrDefault(a => a.Kind == ActionKind.HeroPower);
            if (power != null)
            {
                if (me.HeroId == 1 && (IsStrongest(g, me) || me.HP <= 1 || aliveOpponents == 1)) return power;
                if (me.HeroId == 2 && me.Hand.Count(c => IsSkillCard(c.Type)) >= 2) return power;
            }

            // 1. 解药：弃牌堆有高价值角色牌就回收
            var ant = legal.FirstOrDefault(a => a.Kind == ActionKind.Antidote);
            if (ant != null && g.Discard.Any(c => c.Type == CardType.Arsenal || c.Type == CardType.GrandMinister
                                               || c.Type == CardType.Priest || c.Type == CardType.Exorcist))
                return ant;

            // 2. 带刀侍卫布防：手里侍卫+大臣≥2时开buff（亡语反击威慑）
            var arm = legal.FirstOrDefault(a => a.Kind == ActionKind.ArmGuard);
            if (arm != null && me.Hand.Count(c => c.Type == CardType.Guard || c.Type == CardType.Minister) >= 2)
                return arm;

            // 3. 驱魔：每回合白嫖一次情报/处决机会
            var exo = legal.Where(a => a.Kind == ActionKind.Exorcise).ToList();
            if (exo.Count > 0) return PickTarget(exo, g, me);

            // 4. 扣押：沉默对手的牌
            var sez = legal.Where(a => a.Kind == ActionKind.Seize).ToList();
            if (sez.Count > 0) return PickTarget(sez, g, me);

            // 5. 基础攻击（免费，每回合必用）
            var atk = legal.Where(a => a.Kind == ActionKind.Attack).ToList();
            if (atk.Count > 0) return PickTarget(atk, g, me);

            // 6. 军火库追攻
            var ars = legal.Where(a => a.Kind == ActionKind.ArsenalAttack).ToList();
            if (ars.Count > 0) return PickTarget(ars, g, me);

            // 7. 勒索（2点蓝，讲究收益）
            var ext = legal.Where(a => a.Kind == ActionKind.Extort).ToList();
            if (ext.Count > 0)
            {
                // 斩杀：所有对手都只剩1点HP → 勒索英雄直接清场
                var opps = g.Players.Where(x => x.Alive && x.Seat != seat).ToList();
                if (opps.Count > 0 && opps.All(x => x.HP <= 1))
                {
                    var heroExt = ext.FirstOrDefault(a => a.TargetType == CardType.Hero);
                    if (heroExt != null) return heroExt;
                }
                // 知道有人用过军火库 → 勒索军火库
                var arsExt = ext.FirstOrDefault(a => a.TargetType == CardType.Arsenal);
                if (arsExt != null && _arsenalSeen.Any(s => s)) return arsExt;
                // 否则30%概率盲勒索一个4张的牌型
                if (_rng.NextDouble() < 0.3)
                {
                    var four = ext.Where(a => a.TargetType == CardType.Undead || a.TargetType == CardType.Exorcist
                                           || a.TargetType == CardType.Priest || a.TargetType == CardType.Police
                                           || a.TargetType == CardType.Guard || a.TargetType == CardType.Minister).ToList();
                    if (four.Count > 0) return four[_rng.Next(four.Count)];
                }
            }

            // 7.5 守卫藏牌：有高价值牌且能藏就藏起来防抽
            var hides = legal.Where(a => a.Kind == ActionKind.Hide).ToList();
            if (hides.Count > 0)
            {
                var bestHide = hides.OrderByDescending(a => KeepValue(me.Hand.First(c => c.Id == a.SecondCardId).Type)).First();
                if (KeepValue(me.Hand.First(c => c.Id == bestHide.SecondCardId).Type) >= 5) return bestHide;
            }

            // 8. 贿赂自保：血量危险或手牌见底时，买通最大威胁（1v1决战没有外交，不贿赂）
            if (aliveOpponents >= 2 && (me.HP <= 1 || me.Hand.Count <= 3))
            {
                var bribes = legal.Where(a => a.Kind == ActionKind.Bribe).ToList();
                if (bribes.Count > 0)
                {
                    var bestTarget = bribes.GroupBy(a => a.TargetSeat)
                        .OrderByDescending(gr => ThreatScore(g, me, g.Players[gr.Key])).First().Key;
                    var give = bribes.Where(a => a.TargetSeat == bestTarget)
                        .OrderBy(a => KeepValue(me.Hand.First(c => c.Id == a.SecondCardId).Type)).First();
                    return give;
                }
            }

            // 9. 毒药：1v1决战火力全开；自认最强才斩杀；否则20%概率
            var poi = legal.Where(a => a.Kind == ActionKind.PoisonAttack).ToList();
            if (poi.Count > 0)
            {
                var best = PickTarget(poi, g, me);
                if (aliveOpponents == 1
                    || (IsStrongest(g, me) && g.Players[best.TargetSeat].Hand.Count <= 3)
                    || _rng.NextDouble() < 0.2)
                    return best;
            }

            return legal.First(a => a.Kind == ActionKind.EndTurn);
        }

        // ------------------------------------------------------------------
        // 抉择
        // ------------------------------------------------------------------

        public bool ChooseGuardIntercept(GameEngine2 g, int seat, Card drawn, int attackerSeat)
        {
            var me = g.Players[seat];
            if (drawn.Type == CardType.Guard) return false;
            if (drawn.Type == CardType.Maid) return false;   // 侍女低价值，不浪费侍卫
            // 英雄牌：满血时硬吃1点HP省侍卫，残血才挡
            if (drawn.Type == CardType.Hero) return me.HP < me.MaxHP;
            // 带刀侍卫buff开着：大臣故意送死触发亡语反击，不挡
            if (drawn.Type == CardType.Minister && g.HasArmGuard(seat)) return false;
            return true; // 保护角色牌
        }

        public int ChooseFromDiscard(GameEngine2 g, int seat, List<Card> options)
        {
            var priority = new[]
            {
                CardType.Arsenal, CardType.GrandMinister, CardType.Exorcist, CardType.Police,
                CardType.ArmedGuard, CardType.Warden, CardType.Witch, CardType.Minister,
                CardType.Undead, CardType.Guard, CardType.Maid
            };
            foreach (var t in priority)
            {
                var c = options.FirstOrDefault(x => x.Type == t);
                if (c != null) return c.Id;
            }
            return options[0].Id;
        }

        public int ChooseDeathrattleTarget(GameEngine2 g, int seat, List<int> candidates)
        {
            var me = g.Players[seat];
            return candidates.OrderByDescending(s => ThreatScore(g, me, g.Players[s])).First();
        }

        // ------------------------------------------------------------------
        // 威胁评估（与初代v3.1同源：压制领先者/记仇/军火库记忆/自认最强才斩杀/绝望阎王）
        // ------------------------------------------------------------------

        GameAction PickTarget(List<GameAction> acts, GameEngine2 g, PlayerState me)
        {
            return acts.OrderByDescending(a => ThreatScore(g, me, g.Players[a.TargetSeat])).First();
        }

        float ThreatScore(GameEngine2 g, PlayerState me, PlayerState p)
        {
            float s;
            if (me.Hand.Count <= 2 || me.HP <= 1)
            {
                // 绝望阎王：自己快死了，谁跟我结仇我打谁，输赢另说
                s = p.Hand.Count * 0.5f + _grudge[p.Seat] * 3f + (p.MaxHP - p.HP) * 1f;
            }
            else
            {
                s = p.Hand.Count * 2f;
                s += (p.MaxHP - p.HP) * 2f;   // 血量低=接近斩杀线，优先补刀
                if (IsStrongest(g, me) && p.HP <= 1) s += 8f;  // 斩杀需自认最强
                if (_arsenalSeen[p.Seat]) s += 4f;
                s += _grudge[p.Seat] * 0.35f;
            }
            s += (float)_rng.NextDouble() * 1.5f;
            return s;
        }

        static bool IsStrongest(GameEngine2 g, PlayerState me)
        {
            int maxOpp = g.Players.Where(p => p.Alive && p.Seat != me.Seat)
                                  .Select(p => p.Hand.Count).DefaultIfEmpty(0).Max();
            return me.Hand.Count >= maxOpp;
        }

        /// <summary>牌的价值（贿赂/藏牌决策用，越低越愿意送出去）。</summary>
        static int KeepValue(CardType t)
        {
            switch (t)
            {
                case CardType.Arsenal: return 9;
                case CardType.GrandMinister: return 9;
                case CardType.Witch: return 7;
                case CardType.ArmedGuard: return 7;
                case CardType.Warden: return 6;
                case CardType.Exorcist: return 5;
                case CardType.Police: return 5;
                case CardType.Priest: return 5;
                case CardType.Undead: return 4;
                case CardType.Minister: return 3;
                case CardType.Guard: return 2;
                case CardType.Maid: return 1;
                default: return 1;
            }
        }

        static bool IsSkillCard(CardType t)
        {
            return t == CardType.Exorcist || t == CardType.Police || t == CardType.Witch
                || t == CardType.Arsenal || t == CardType.Minister || t == CardType.GrandMinister
                || t == CardType.ArmedGuard || t == CardType.Warden;
        }
    }
}
