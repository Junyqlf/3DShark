using System;
using System.Collections.Generic;
using System.Linq;

namespace SandKill.Core
{
    /// <summary>
    /// 3D杀 规则引擎（纯C#，不依赖Unity）。
    ///
    /// 规则要点：
    /// - 开局每人 9 张随机 + 1 张保底工人。
    /// - 每回合：基础攻击1次；军火库/驱魔师/警察每张每回合可用1次；女巫毒药/解药整局各1次。
    /// - 攻击=随机抽目标一张手牌进弃牌堆；亡灵免疫返回；侍卫可挡刀；牧师被抽走触发亡语。
    /// - 警察扣押：牌离开目标手牌，目标下个回合结束后归还（沉默1回合）。
    /// - 失去所有工人→出局，手牌全部进弃牌堆；最后存活者获胜。
    ///
    /// 真人与引擎的交互：UI 读取 GetLegalActions 构建按钮，点击后调用 ApplyAction；
    /// 若引擎产生 Pending（真人需要抉择），UI 展示选项后调用 SubmitGuardChoice / SubmitDiscardPick。
    /// </summary>
    public class GameEngine
    {
        public List<PlayerState> Players = new List<PlayerState>();
        public List<Card> Discard = new List<Card>();
        /// <summary>开局后剩余未发的牌（初版规则不使用，仅作展示与守恒校验）。</summary>
        public List<Card> SideDeck = new List<Card>();
        public int Current;                       // 当前行动玩家座位
        public PendingChoice Pending;             // 非null表示等待真人抉择
        public bool GameOver;
        public int WinnerSeat = -1;
        public bool BasicAttackUsed;
        public int TurnNumber = 1;

        public IPlayerBrain[] Brains;

        /// <summary>对局日志事件（UI订阅此事件追加显示）。</summary>
        public event Action<string> OnLog;

        /// <summary>
        /// 敌对行动事件（攻击者座位, 受害者座位, 行动类型）。
        /// 攻击/军火库/毒药/驱魔/扣押时触发。AI订阅它来记仇、记忆公开亮过的军火库。
        /// </summary>
        public event Action<int, int, ActionKind> OnHostile;

        void RaiseHostile(PlayerState attacker, PlayerState victim, ActionKind kind)
        {
            if (OnHostile != null) OnHostile(attacker.Seat, victim.Seat, kind);
        }

        /// <summary>完整日志历史（UI初始化时可补读引擎构造期间的日志）。</summary>
        public readonly List<string> History = new List<string>();

        readonly Random _rng;

        class HeldCard
        {
            public Card Card;
            public int OwnerSeat;
            public int TurnsLeft;   // 拥有者的回合结束次数，归零归还
        }
        readonly List<HeldCard> _held = new List<HeldCard>();

        /// <summary>被 seat 玩家扣押中的牌数（UI显示用）。</summary>
        public int HeldCountOf(int seat) { return _held.Count(h => h.OwnerSeat == seat); }

        /// <summary>测试用：全场卡牌总数守恒校验。</summary>
        public int TotalCards
        {
            get { return Players.Sum(p => p.Hand.Count) + Discard.Count + _held.Count + SideDeck.Count; }
        }

        // ------------------------------------------------------------------
        // 初始化
        // ------------------------------------------------------------------

        /// <summary>
        /// deck: 卡组构成（每种牌数量）。names: 玩家名。brains: 每个座位的决策者（真人传HumanBrain占位）。
        /// </summary>
        public GameEngine(Dictionary<CardType, int> deck, string[] names, IPlayerBrain[] brains, int seed)
        {
            _rng = new Random(seed);
            Brains = brains;

            if (names.Length < 2) throw new ArgumentException("至少需要2名玩家");
            if (!deck.ContainsKey(CardType.Worker) || deck[CardType.Worker] < names.Length)
                throw new ArgumentException("工人数量必须不少于玩家数（每人保底1张）");

            // 1. 组牌
            var pool = new List<Card>();
            int id = 1;
            foreach (var kv in deck)
                for (int i = 0; i < kv.Value; i++)
                    pool.Add(new Card(id++, kv.Key));

            // 2. 创建玩家
            for (int s = 0; s < names.Length; s++)
            {
                Players.Add(new PlayerState
                {
                    Seat = s,
                    Name = names[s],
                    IsHuman = brains[s].IsHuman
                });
            }

            // 3. 每人先拿1张保底工人（不参与随机抽取）
            foreach (var p in Players)
            {
                var w = pool.First(c => c.Type == CardType.Worker);
                pool.Remove(w);
                p.Hand.Add(w);
            }

            // 4. 洗牌，每人再抽9张
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
            }
            foreach (var p in Players)
                for (int i = 0; i < 9 && pool.Count > 0; i++)
                {
                    p.Hand.Add(pool[0]);
                    pool.RemoveAt(0);
                }

            // 5. 剩余牌放到一边（本版本不再使用）
            SideDeck = pool;

            Current = 0;
            Log("====== 3D杀 对局开始 ======");
            Log("卡组共 " + deck.Values.Sum() + " 张，" + names.Length + " 名玩家，每人 9 随机 + 1 保底工人。");
            LogTurn();
        }

        // ------------------------------------------------------------------
        // 日志
        // ------------------------------------------------------------------

        void Log(string msg)
        {
            History.Add(msg);
            if (OnLog != null) OnLog(msg);
        }

        void LogTurn()
        {
            Log("—— 第" + TurnNumber + "轮 · " + Players[Current].Name + " 的回合 ——");
        }

        // ------------------------------------------------------------------
        // 合法行动枚举（UI / AI 共用）
        // ------------------------------------------------------------------

        public List<GameAction> GetLegalActions(int seat)
        {
            var list = new List<GameAction>();
            if (GameOver || Pending != null) return list;
            var me = Players[seat];
            if (!me.Alive || seat != Current) return list;

            var targets = Players.Where(p => p.Alive && p.Seat != seat && p.Hand.Count > 0).ToList();

            if (!BasicAttackUsed)
                foreach (var t in targets)
                    list.Add(new GameAction { Kind = ActionKind.Attack, TargetSeat = t.Seat });

            foreach (var c in me.Hand)
            {
                switch (c.Type)
                {
                    case CardType.Arsenal:
                        if (!c.UsedSkillThisTurn)
                            foreach (var t in targets)
                                list.Add(new GameAction { Kind = ActionKind.ArsenalAttack, TargetSeat = t.Seat, SourceCardId = c.Id });
                        break;
                    case CardType.Exorcist:
                        if (!c.UsedSkillThisTurn)
                            foreach (var t in targets)
                                list.Add(new GameAction { Kind = ActionKind.Exorcise, TargetSeat = t.Seat, SourceCardId = c.Id });
                        break;
                    case CardType.Police:
                        if (!c.UsedSkillThisTurn)
                            foreach (var t in targets)
                                list.Add(new GameAction { Kind = ActionKind.Seize, TargetSeat = t.Seat, SourceCardId = c.Id });
                        break;
                    case CardType.Witch:
                        if (!c.PoisonUsed)
                            foreach (var t in targets)
                                list.Add(new GameAction { Kind = ActionKind.PoisonAttack, TargetSeat = t.Seat, SourceCardId = c.Id });
                        if (!c.AntidoteUsed && Discard.Any(d => d.Type.IsRole()))
                            list.Add(new GameAction { Kind = ActionKind.Antidote, SourceCardId = c.Id });
                        break;
                }
            }

            list.Add(new GameAction { Kind = ActionKind.EndTurn });
            return list;
        }

        // ------------------------------------------------------------------
        // 行动执行
        // ------------------------------------------------------------------

        public void ApplyAction(GameAction a)
        {
            if (GameOver || Pending != null) return;
            var me = Players[Current];
            if (!me.Alive) return;

            if (a.Kind == ActionKind.EndTurn) { EndTurn(); return; }

            var target = (a.TargetSeat >= 0 && a.TargetSeat < Players.Count) ? Players[a.TargetSeat] : null;
            Card src = a.SourceCardId >= 0 ? me.Hand.FirstOrDefault(c => c.Id == a.SourceCardId) : null;

            switch (a.Kind)
            {
                case ActionKind.Attack:
                    if (target == null) return;
                    BasicAttackUsed = true;
                    Log(me.Name + " 对 " + target.Name + " 发动攻击！");
                    RaiseHostile(me, target, ActionKind.Attack);
                    ResolveAttack(me, target);
                    break;

                case ActionKind.ArsenalAttack:
                    if (target == null || src == null || src.UsedSkillThisTurn) return;
                    src.UsedSkillThisTurn = true;
                    Log(me.Name + " 借助【军火库】对 " + target.Name + " 追加攻击！");
                    RaiseHostile(me, target, ActionKind.ArsenalAttack);
                    ResolveAttack(me, target);
                    break;

                case ActionKind.PoisonAttack:
                    if (target == null || src == null || src.PoisonUsed) return;
                    src.PoisonUsed = true;
                    Log(me.Name + " 使用【女巫·毒药】，对 " + target.Name + " 追加攻击！");
                    RaiseHostile(me, target, ActionKind.PoisonAttack);
                    ResolveAttack(me, target);
                    break;

                case ActionKind.Exorcise:
                    if (target == null || src == null || src.UsedSkillThisTurn) return;
                    src.UsedSkillThisTurn = true;
                    RaiseHostile(me, target, ActionKind.Exorcise);
                    ResolveExorcise(me, target);
                    break;

                case ActionKind.Seize:
                    if (target == null || src == null || src.UsedSkillThisTurn) return;
                    src.UsedSkillThisTurn = true;
                    RaiseHostile(me, target, ActionKind.Seize);
                    ResolveSeize(me, target);
                    break;

                case ActionKind.Antidote:
                    if (src == null || src.AntidoteUsed) return;
                    var opts = Discard.Where(d => d.Type.IsRole()).ToList();
                    if (opts.Count == 0) return;
                    src.AntidoteUsed = true;
                    Log(me.Name + " 使用【女巫·解药】。");
                    PickFromDiscard(me, opts, "女巫·解药");
                    break;
            }
        }

        // ------------------------------------------------------------------
        // 攻击结算（基础/军火库/毒药共用）
        // ------------------------------------------------------------------

        void ResolveAttack(PlayerState attacker, PlayerState victim)
        {
            if (victim.Hand.Count == 0) { Log(victim.Name + " 没有手牌，攻击落空。"); return; }

            var drawn = victim.Hand[_rng.Next(victim.Hand.Count)];

            // 1. 亡灵免疫
            if (drawn.Type == CardType.Undead)
            {
                Log("抽中了【亡灵】！免疫攻击，返回 " + victim.Name + " 的手牌。");
                return;
            }

            // 2. 侍卫挡刀抉择
            var guard = victim.FirstOf(CardType.Guard);
            if (guard != null)
            {
                var brain = Brains[victim.Seat];
                if (brain.IsHuman)
                {
                    Pending = new PendingChoice
                    {
                        Kind = ChoiceKind.GuardIntercept,
                        Seat = victim.Seat,
                        DrawnCard = drawn,
                        AttackerSeat = attacker.Seat
                    };
                    Log(victim.Name + " 有【侍卫】，正在决定是否挡刀……");
                    return;
                }
                bool use = brain.ChooseGuardIntercept(this, victim.Seat, drawn);
                Log(use
                    ? victim.Name + " 的【侍卫】挺身而出！"
                    : victim.Name + " 选择保留【侍卫】。");
                ResolveGuardOutcome(victim, drawn, use);
                return;
            }

            ResolveGuardOutcome(victim, drawn, false);
        }

        void ResolveGuardOutcome(PlayerState victim, Card drawn, bool useGuard)
        {
            if (useGuard)
            {
                var guard = victim.FirstOf(CardType.Guard);
                victim.Hand.Remove(guard);
                Discard.Add(guard);
                Log("【侍卫】代替【" + drawn.Type.Name() + "】进入弃牌堆。");
                return;
            }

            victim.Hand.Remove(drawn);
            Discard.Add(drawn);
            Log("【" + drawn.Type.Name() + "】被抽走，进入弃牌堆。");

            // 先出局判定（死人不能触发亡语），再牧师亡语
            CheckElimination(victim);

            if (drawn.Type == CardType.Priest && victim.Alive && !GameOver)
            {
                Log("【牧师】亡语触发！");
                var opts = Discard.Where(d => d.Type.IsRole() && d.Id != drawn.Id).ToList();
                if (opts.Count > 0)
                    PickFromDiscard(victim, opts, "牧师亡语");
                else
                    Log("弃牌堆中没有可回收的角色牌。");
            }
        }

        // ------------------------------------------------------------------
        // 驱魔师 / 警察
        // ------------------------------------------------------------------

        void ResolveExorcise(PlayerState exorcist, PlayerState victim)
        {
            if (victim.Hand.Count == 0) { Log(victim.Name + " 没有手牌。"); return; }
            var drawn = victim.Hand[_rng.Next(victim.Hand.Count)];

            if (drawn.Type == CardType.Undead)
            {
                victim.Hand.Remove(drawn);
                Discard.Add(drawn);
                Log(exorcist.Name + " 的【驱魔师】抽中了【亡灵】——当场处决！");
            }
            else
            {
                // 简化版信息公开在日志中（单机对AI，无作弊问题）
                Log(exorcist.Name + " 的【驱魔师】抽到了一张牌，不是亡灵，归还。（情报：那是【" + drawn.Type.Name() + "】）");
            }
        }

        void ResolveSeize(PlayerState police, PlayerState victim)
        {
            if (victim.Hand.Count == 0) { Log(victim.Name + " 没有手牌。"); return; }
            var drawn = victim.Hand[_rng.Next(victim.Hand.Count)];
            victim.Hand.Remove(drawn);
            _held.Add(new HeldCard { Card = drawn, OwnerSeat = victim.Seat, TurnsLeft = 1 });
            Log(police.Name + " 的【警察】扣押了 " + victim.Name + " 的一张手牌，沉默1回合。（是【" + drawn.Type.Name() + "】）");
            // 扣押可能导致目标失去最后的工人
            CheckElimination(victim);
        }

        // ------------------------------------------------------------------
        // 弃牌堆选牌（牧师亡语 / 女巫解药）
        // ------------------------------------------------------------------

        void PickFromDiscard(PlayerState p, List<Card> options, string reason)
        {
            var brain = Brains[p.Seat];
            if (brain.IsHuman)
            {
                Pending = new PendingChoice
                {
                    Kind = ChoiceKind.PickFromDiscard,
                    Seat = p.Seat,
                    Options = options,
                    Reason = reason
                };
                return;
            }
            var pick = brain.ChooseFromDiscard(this, p.Seat, options, reason);
            CompletePick(p, pick, reason);
        }

        void CompletePick(PlayerState p, Card pick, string reason)
        {
            Discard.Remove(pick);
            p.Hand.Add(pick);
            Log(p.Name + " 通过【" + reason + "】从弃牌堆拿回了【" + pick.Type.Name() + "】。");
        }

        // ------------------------------------------------------------------
        // 真人抉择提交
        // ------------------------------------------------------------------

        public void SubmitGuardChoice(bool useGuard)
        {
            if (Pending == null || Pending.Kind != ChoiceKind.GuardIntercept) return;
            var pc = Pending;
            Pending = null;
            var victim = Players[pc.Seat];
            Log(useGuard
                ? victim.Name + " 的【侍卫】挺身而出！"
                : victim.Name + " 选择保留【侍卫】。");
            ResolveGuardOutcome(victim, pc.DrawnCard, useGuard);
        }

        public void SubmitDiscardPick(int cardId)
        {
            if (Pending == null || Pending.Kind != ChoiceKind.PickFromDiscard) return;
            var pc = Pending;
            Pending = null;
            var pick = pc.Options.FirstOrDefault(c => c.Id == cardId);
            if (pick == null) return;
            CompletePick(Players[pc.Seat], pick, pc.Reason);
        }

        // ------------------------------------------------------------------
        // 回合结束 / 出局 / 胜负
        // ------------------------------------------------------------------

        void EndTurn()
        {
            var me = Players[Current];

            // 归还被扣押的牌（拥有者回合结束时计时）
            foreach (var h in _held.Where(h => h.OwnerSeat == me.Seat).ToList())
            {
                h.TurnsLeft--;
                if (h.TurnsLeft <= 0)
                {
                    _held.Remove(h);
                    if (me.Alive)
                    {
                        me.Hand.Add(h.Card);
                        Log(me.Name + " 被扣押的【" + h.Card.Type.Name() + "】已归还手牌。");
                    }
                    else
                    {
                        Discard.Add(h.Card);
                    }
                }
            }

            // 轮到下一个存活玩家；座位号回绕（新座位<旧座位）视为新一轮
            // ——不依赖0号位存活，任何玩家出局后轮次照常刷新
            int n = Players.Count, old = Current, seat = Current;
            do { seat = (seat + 1) % n; } while (!Players[seat].Alive && seat != Current);
            Current = seat;
            if (seat < old) TurnNumber++;

            // 回合开始重置
            BasicAttackUsed = false;
            foreach (var c in Players[Current].Hand) c.UsedSkillThisTurn = false;

            if (!GameOver) LogTurn();
        }

        void CheckElimination(PlayerState p)
        {
            if (GameOver || !p.Alive || p.WorkerCount > 0) return;

            p.Alive = false;
            foreach (var c in p.Hand.ToList()) Discard.Add(c);
            p.Hand.Clear();
            foreach (var h in _held.Where(h => h.OwnerSeat == p.Seat).ToList())
            {
                _held.Remove(h);
                Discard.Add(h.Card);
            }
            Log("☠ " + p.Name + " 失去了所有工人，出局！");

            var alive = Players.Where(x => x.Alive).ToList();
            if (alive.Count == 1)
            {
                GameOver = true;
                WinnerSeat = alive[0].Seat;
                Log("🏆 " + alive[0].Name + " 是最后的存活者，获得胜利！");
            }
        }
    }
}
