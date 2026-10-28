using System;
using System.Collections.Generic;
using System.Linq;

namespace SandKill.Core
{
    /// <summary>
    /// AI 决策 v3（启发式，同步决策）。
    ///
    /// 目标选择（FFA 混战原则）：
    ///   常态威胁分 = 手牌数×2（牌多=血厚=最大威胁）
    ///              + 濒死加分（手牌≤2 → +8，顺手斩杀减员）
    ///              + 已知军火库持有者 +4（公开用过军火库的全场都记得）
    ///              + 仇恨值×0.5（记仇：谁打过我，优先还回去）
    ///              - 被扣押减分；+ 随机抖动±1.5
    ///
    ///   绝望模式（自己手牌≤3，获胜无望）：仇恨值成为主导（×3），威胁分退居其次——
    ///   我当不了赢家，就当阎王：谁整我整得最狠，我就拖谁下水。
    ///
    /// 记忆来源：引擎的 OnHostile 事件（谁对谁发动了攻击/军火库/毒药/驱魔/扣押，全场公开信息，不作弊）。
    ///
    /// 自保策略：
    /// - 侍卫：角色牌必保，但废牌不挡——双药用光的女巫、侍卫自己；工人≤2时保工人。
    /// - 毒药：目标濒死（≤3张）时用于斩杀，否则20%概率使用；下毒格外招仇恨（仇恨值+2）。
    /// - 解药：弃牌堆有高价值角色（军火库/牧师/驱魔师）才用。
    /// - 回收优先级：军火库 > 牧师 > 驱魔师 > 警察 > 女巫 > 亡灵 > 侍卫。
    /// </summary>
    public class AIBrain : IPlayerBrain
    {
        public bool IsHuman { get { return false; } }

        readonly Random _rng;
        readonly int _seat;
        readonly int[] _grudge;        // 仇恨值：谁攻击过我，我记得
        readonly bool[] _arsenalSeen;  // 谁当众亮过军火库，我记得

        public AIBrain(int seed, int seat, int playerCount)
        {
            _rng = new Random(seed);
            _seat = seat;
            _grudge = new int[playerCount];
            _arsenalSeen = new bool[playerCount];
        }

        /// <summary>引擎敌对行动通知：记仇 + 记忆公开亮过的军火库。</summary>
        public void NoteHostile(int attacker, int victim, ActionKind kind)
        {
            if (kind == ActionKind.ArsenalAttack) _arsenalSeen[attacker] = true;
            if (victim == _seat && attacker != _seat)
                _grudge[attacker] += (kind == ActionKind.PoisonAttack ? 2 : 1); // 下毒格外招恨
        }

        /// <summary>测试/调试/未来UI展示用：我对某座位的仇恨值。</summary>
        public int GrudgeOf(int seat) { return _grudge[seat]; }

        /// <summary>测试/调试/未来UI展示用：我是否知道某座位持有军火库。</summary>
        public bool KnowsArsenal(int seat) { return _arsenalSeen[seat]; }

        public GameAction ChooseAction(GameEngine g, int seat, List<GameAction> legal)
        {
            // 1. 解药：弃牌堆有高价值角色才用
            var ant = legal.Where(a => a.Kind == ActionKind.Antidote).ToList();
            if (ant.Count > 0 && g.Discard.Any(d =>
                    d.Type == CardType.Arsenal || d.Type == CardType.Priest || d.Type == CardType.Exorcist))
                return ant[0];

            // 2. 驱魔师：压领先者，顺便猎杀亡灵
            var exo = legal.Where(a => a.Kind == ActionKind.Exorcise).ToList();
            if (exo.Count > 0) return PickTarget(exo, g, g.Players[seat]);

            // 3. 警察：压领先者
            var sez = legal.Where(a => a.Kind == ActionKind.Seize).ToList();
            if (sez.Count > 0) return PickTarget(sez, g, g.Players[seat]);

            // 4. 基础攻击：打威胁/仇恨分最高的目标
            var atk = legal.Where(a => a.Kind == ActionKind.Attack).ToList();
            if (atk.Count > 0) return PickTarget(atk, g, g.Players[seat]);

            // 5. 军火库追加攻击：同攻击逻辑
            var ars = legal.Where(a => a.Kind == ActionKind.ArsenalAttack).ToList();
            if (ars.Count > 0) return PickTarget(ars, g, g.Players[seat]);

            // 6. 毒药：1v1决战火力全开不留后手；否则自认最强才斩杀，或20%概率使用
            var poi = legal.Where(a => a.Kind == ActionKind.PoisonAttack).ToList();
            if (poi.Count > 0)
            {
                var meNow = g.Players[seat];
                var best = PickTarget(poi, g, meNow);
                int aliveOpponents = g.Players.Count(x => x.Alive && x.Seat != seat);
                if (aliveOpponents == 1
                    || (IsStrongest(g, meNow) && g.Players[best.TargetSeat].Hand.Count <= 3)
                    || _rng.NextDouble() < 0.2)
                    return best;
            }

            return legal.First(a => a.Kind == ActionKind.EndTurn);
        }

        /// <summary>我是否是全场战斗力最强（手牌数最多）的玩家——斩杀算法的前置条件。</summary>
        static bool IsStrongest(GameEngine g, PlayerState me)
        {
            int maxOther = g.Players.Where(x => x.Alive && x.Seat != me.Seat).Max(x => x.Hand.Count);
            return me.Hand.Count >= maxOther;
        }

        GameAction PickTarget(List<GameAction> acts, GameEngine g, PlayerState me)
        {
            return acts.OrderByDescending(a => ThreatScore(g, me, g.Players[a.TargetSeat])).First();
        }

        /// <summary>
        /// 威胁+仇恨综合评分，分高者优先进攻。
        /// 常态：压制领先者，仇恨半权重并入。
        /// 绝望（自己手牌≤3）：仇恨主导——获胜无望时化身阎王，决定谁能赢。
        /// </summary>
        float ThreatScore(GameEngine g, PlayerState me, PlayerState p)
        {
            float s;
            if (me.Hand.Count <= 2)
            {
                // 绝望模式：我打谁谁输，专挑最恨的
                s = p.Hand.Count * 0.5f + _grudge[p.Seat] * 3f;
            }
            else
            {
                s = p.Hand.Count * 2f;                          // 牌多=血厚=最大威胁
                // 斩杀只在我自认全场最强时才有意义：
                // 否则减员弱者，等于替真正的领先者扫清了制衡他的力量
                if (IsStrongest(g, me) && p.Hand.Count <= 2) s += 8f;
                if (_arsenalSeen[p.Seat]) s += 4f;              // 已知军火库持有者：重点关照
                s += _grudge[p.Seat] * 0.35f;                    // 记仇
            }
            if (g.HeldCountOf(p.Seat) > 0) s -= 1f;             // 有牌被扣押，暂时虚弱
            s += (float)_rng.NextDouble() * 1.5f;               // 抖动，行为更自然
            return s;
        }

        public bool ChooseGuardIntercept(GameEngine g, int seat, Card drawn)
        {
            var me = g.Players[seat];

            // 废牌不挡：双药用光的女巫、侍卫自己（用侍卫保护侍卫等于白送一张侍卫）
            if (drawn.Type == CardType.Witch && drawn.PoisonUsed && drawn.AntidoteUsed) return false;
            if (drawn.Type == CardType.Guard) return false;

            if (drawn.Type.IsRole()) return true;      // 角色牌必保
            if (me.WorkerCount <= 2) return true;      // 工人告急时保工人
            return false;                              // 工人充足时硬吃伤害
        }

        public Card ChooseFromDiscard(GameEngine g, int seat, List<Card> options, string reason)
        {
            foreach (var t in PickPriority)
            {
                var c = options.FirstOrDefault(o => o.Type == t);
                if (c != null) return c;
            }
            return options[0];
        }

        static readonly CardType[] PickPriority =
        {
            CardType.Arsenal, CardType.Priest, CardType.Exorcist,
            CardType.Police, CardType.Witch, CardType.Undead, CardType.Guard
        };
    }

    /// <summary>真人占位：引擎只用它的 IsHuman 标记，决策方法永远不会被调用（由UI驱动）。</summary>
    public class HumanBrain : IPlayerBrain
    {
        public bool IsHuman { get { return true; } }

        public GameAction ChooseAction(GameEngine g, int seat, List<GameAction> legal)
        {
            throw new NotSupportedException("真人决策应由UI驱动，不应调用此方法");
        }

        public bool ChooseGuardIntercept(GameEngine g, int seat, Card drawn)
        {
            throw new NotSupportedException("真人决策应由UI驱动，不应调用此方法");
        }

        public Card ChooseFromDiscard(GameEngine g, int seat, List<Card> options, string reason)
        {
            throw new NotSupportedException("真人决策应由UI驱动，不应调用此方法");
        }
    }
}
