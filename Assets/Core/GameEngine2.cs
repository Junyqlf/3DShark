using System;
using System.Collections.Generic;
using System.Linq;

namespace SandKill.Core
{
    /// <summary>
    /// 2代规则引擎：英雄HP/MP系统（2026-10-13 工人回归牌库×3：行动点+1被动）、法力值限制技能、
    /// 每2回合免费抽1张牌（牌堆耗尽即止，不洗牌）、天赋"每2回合回1点HP"、
    /// 新牌：带刀侍卫/大臣/军机大臣。
    /// 与初代引擎 GameEngine 完全独立，互不影响。
    ///
    /// 核心规则：
    /// - 英雄牌永不离开手牌：攻击抽中/警察扣押/勒索打中英雄牌，一律改为扣1点HP
    /// - HP归零出局；牧师亡语/女巫解药不能回收英雄牌，牧师亡语不能回收牧师
    /// - 普攻耗1行动点，每回合1次；主动技能耗蓝（数值全部在 CardDefs 表，引擎只引用）
    /// - 贿赂结盟：到贿赂者的下回合开始前，被贿赂者不能攻击贿赂者（含军火库追攻/毒药）
    /// </summary>
    public class GameEngine2
    {
        // ---- 蓝耗（普攻1行动点，2026-09-29起）----
        // 单一数据源：数值全部来自 CardDefs 表，调平衡时改表即可，引擎与界面不会漂移。
        // const 改成了 static readonly——值在运行时从表读出，语义不变。
        public const int COST_BASIC_ATTACK = 1;   // 普攻耗1行动点（2026-09-29起，不再免费）
        public static readonly int COST_EXORCISE = CardDefs.Of(CardType.Exorcist).SkillCost;
        public static readonly int COST_SEIZE = CardDefs.Of(CardType.Police).SkillCost;
        public static readonly int COST_ANTIDOTE = CardDefs.Of(CardType.Witch).SubSkillCost;
        public static readonly int COST_ARSENAL = CardDefs.Of(CardType.Arsenal).SkillCost;
        public static readonly int COST_BRIBE = CardDefs.Of(CardType.Minister).SkillCost;
        public static readonly int COST_ARMGUARD = CardDefs.Of(CardType.ArmedGuard).SkillCost;
        public static readonly int COST_HIDE = CardDefs.Of(CardType.Warden).SkillCost;
        public static readonly int COST_POISON = CardDefs.Of(CardType.Witch).SkillCost;
        public static readonly int COST_EXTORT = CardDefs.Of(CardType.GrandMinister).SkillCost;
        public const int COST_CRAFT = 1;   // 合成：消耗1行动点（2026-10-06）
        public const int SYNTH_HP = 2;     // 合成牌有2点生命（女祭司祈祷的转化来源）
        public const int CRAFT_SHIELD_TURNS = 2;    // 方案C护盾（2026-10-13）：合成护盾持续N个自己的回合
        public const int CRAFT_SHIELD_CHARGES = 1;  // 方案C护盾：可吸收的伤害次数（吸满即消散；到期也消散）
        public static readonly int COST_SYNTH_SKILL = CardDefs.Of(CardType.Priestess).SkillCost;   // 女祭司技能/典狱长/审判官蓝耗（表驱动，均1）
        public static readonly int COST_BOMB = CardDefs.Of(CardType.Terrorist).SkillCost;          // 恐怖分子安放炸弹蓝耗（2026-10-13 改版）
        public const int TRIUMPH_SHIELD_TURNS = 2;  // 凯旋护盾持续回合数（对标方案C合成护盾节拍）
        public const int XP_LEVEL_2 = 6;            // 英雄升级（2026-10-21 用户裁定）：消灭6张牌升2级
        public const int XP_LEVEL_3 = 8;            // 英雄升级（2026-10-21 用户裁定）：消灭8张牌升3级（升2后再攒2点）
        public const int MAX_HERO_LEVEL = 2;        // HeroLevel 语义=已升级次数：0=初始(1级) / 1=升过1次(2级) / 2=升满(3级)；每局最多升2次
        public const int LEVEL_SHIELD_TURNS = 2;    // 升级奖励护盾持续回合数（方案C同款节拍；草样里直接复用 CRAFT_SHIELD_TURNS，单列常量便于将来分叉）

        /// <summary>合成配方（2026-10-06）：合成牌 → 需要的3张牌；null = 任意非英雄牌。合成另耗1行动点，奖励抽1张牌。</summary>
        public static readonly Dictionary<CardType, CardType?[]> CraftRecipes = new Dictionary<CardType, CardType?[]>
        {
            { CardType.Priestess,  new CardType?[] { CardType.GreenApple, CardType.Witch, CardType.Priest } },
            { CardType.Valkyrie,   new CardType?[] { CardType.Guard, CardType.Maid, null } },
            { CardType.Paladin,    new CardType?[] { CardType.Guard, CardType.Priest, null } },
            { CardType.Jailer,     new CardType?[] { CardType.Warden, CardType.Police, CardType.Minister } },
            { CardType.Inquisitor, new CardType?[] { CardType.Exorcist, CardType.Priest, CardType.Maid } },
        };

        /// <summary>英雄模板。未来加新英雄 = 在这张表里加一行 + 在ApplyAction的HeroPower分支里实现。</summary>
        public class HeroSpec
        {
            public int Id; public string Name; public int HP; public int MP;
            public bool HealTalent;   // 医师：自受伤起每消耗6行动点回1点HP（受伤重置计数）
            public bool HasPower;     // 有主动技（整局限1次）
            public int ExtraAttackEarly; // 攻城锤：自己的前N个回合每回合额外1次普攻机会（旧窗口语义，2026-10-14 起被 ExtraAttackTurns 取代；保留作实验旋钮的门槛判断）
            public int EarlyMpBonus;     // 攻城锤：自己的前N个（=ExtraAttackEarly）回合行动点+1（2026-10-09 用户裁定；2026-10-14 用户裁定取消=0：窗口挪到7/8回合后前期蓝不再加成）
            public int[] ExtraAttackTurns; // 攻城锤：固定回合集合，各触发1次额外普攻机会（2026-10-14 用户裁定=回合7、8；2026-10-20 起默认改 SiegeOnLevelUp，本字段仅实验旋钮非 null 时生效；判定优先级：SiegeFixedTurns > SiegeExtraTurns > ExtraAttackTurns > SiegeOnLevelUp > ExtraAttackEarly）
            public bool SiegeOnLevelUp;    // 攻城锤（2026-10-20 用户裁定"7、8回合太奇怪了，改为升级后2回合能普攻2次"）：英雄升级当回合与下个自己的回合各拥有1次额外普攻机会（窗口=HeroLevel≥1 且 OwnTurnCount≤LevelTurn+1）
            public bool FocusFire;    // 强攻：连续攻击同一玩家3次（可跨回合，中间不能打别人）追加1次攻击
            public bool PriestSacrifice; // 牺牲：自己的牧师可以像侍卫一样挺身而出挡刀
            public bool WitchHunt;    // 猎巫行动：自己的驱魔师抽中对手的女巫时将其处决
            public bool MaidCharm;    // 美人计（2026-10-19 最终版=被动）：用大臣结盟时若交出侍女，对方被魅惑——持有者受攻击时攻击转移到魅惑目标身上走完整伤害判定（到持有者下回合开始过期）
            public bool Triumph;      // 凯旋：自己淘汰一名玩家时获得2回合护盾，并可从其手牌中挑1张（不含英雄牌）
            public bool ColdHunt;     // 冷酷追猎（2026-10-19 重做，原"骑士精神"）：本回合行动点耗尽（不足一次普攻）且未普攻过时，回合结束随机处决一名对手手中被封印的牌（任何人的封印都算）
            public bool HasDeathblow; // 致命一击（2026-10-19 重做=被动，当晚加强手牌上限4→5）：你攻击HP=1且手牌（不含英雄牌）≤5的玩家时，这次攻击必定命中其英雄
            public string Desc;
        }
        public static readonly HeroSpec[] HeroRoster =
        {
            new HeroSpec { Id = 0, Name = "医师", HP = 3, MP = 3, HealTalent = true,
                Desc = "天赋：自受伤起，每消耗6点行动点回复1点HP（再次受伤重新计算）" },
            new HeroSpec { Id = 1, Name = "police station", HP = 3, MP = 3, HasPower = true,
                Desc = "主动技（限1次）：所有敌人的下个回合无法使用任何技能（含被动）" },
            new HeroSpec { Id = 2, Name = "save", HP = 3, MP = 3, HasPower = true,
                Desc = "主动技（限1次）：本回合你的技能无视冷却与限定次数——回合技可连发、限定技可用掉（蓝耗照付）" },
            new HeroSpec { Id = 3, Name = "攻城锤", HP = 3, MP = 3, ExtraAttackEarly = 2, EarlyMpBonus = 0, SiegeOnLevelUp = true,
                Desc = "被动：英雄升级后（累计消灭8张牌），升级当回合与下个自己的回合，每回合拥有1次额外的普攻机会（行动点不加成）；开局攻击倾向打你的下家。开局额外持有【调度券】×2（开局换牌：1券可将1张手牌与牌堆顶交换，换哪张不公开）" },
            new HeroSpec { Id = 4, Name = "强攻", HP = 3, MP = 3, FocusFire = true,
                Desc = "被动：连续对同一玩家完成3次攻击（可跨回合，中间不能打别人）后，针对同一目标追加1次免费攻击（不耗行动点）；触发后进入1回合冷却——触发回合与下一回合的命中不计入新连击" },
            new HeroSpec { Id = 5, Name = "猎巫行动", HP = 3, MP = 3, WitchHunt = true,
                Desc = "被动：你的驱魔师抽中对手的女巫时将其处决（不再只是验亡灵）。开局额外持有【调度券】×1（开局换牌：1券可将1张手牌与牌堆顶交换，换哪张不公开）" },
            new HeroSpec { Id = 6, Name = "牺牲", HP = 3, MP = 3, PriestSacrifice = true,
                Desc = "被动：你的【牧师】可以像【侍卫】一样挺身而出替你挡下攻击；挡刀被消耗也照常触发牧师亡语（从弃牌堆回收一张角色牌，不含牧师和英雄）" },
            new HeroSpec { Id = 7, Name = "美人计", HP = 3, MP = 3, MaidCharm = true,
                Desc = "被动：你用大臣结盟时若把【侍女】交给对手，对方会被魅惑——直到你的下个回合开始，你受到的攻击由魅惑目标代替承受（走完整的伤害判定流程）。开局额外持有【调度券】×3（开局换牌：1券可将1张手牌与牌堆顶交换，换哪张不公开）" },
            new HeroSpec { Id = 8, Name = "凯旋", HP = 3, MP = 3, Triumph = true,
                Desc = "被动：你每淘汰一名玩家，获得1点持续2回合的【护盾】（吸收1点伤害），并可从该玩家剩余手牌中挑选1张（不含英雄牌）" },
            new HeroSpec { Id = 9, Name = "冷酷追猎", HP = 3, MP = 3, ColdHunt = true,
                Desc = "被动：你的回合结束时，若本回合尚未普攻且剩余行动点不足一次普攻，随机处决一名对手手中被封印的牌（任何人造成的封印都算）。不耗蓝、不占普攻槽。开局额外持有【调度券】×3（开局换牌：1券可将1张手牌与牌堆顶交换，换哪张不公开）" },
            new HeroSpec { Id = 10, Name = "致命一击", HP = 3, MP = 3, HasDeathblow = true,
                Desc = "被动：你攻击生命值为1且手牌数（不含英雄牌）小于等于5的玩家时，这次攻击必定命中其英雄（走攻击判定时直接命中）。开局额外持有【调度券】×3（开局换牌：1券可将1张手牌与牌堆顶交换，换哪张不公开）" },
        };

        // ---- 勒索可选的牌型 ----
        public static readonly CardType[] ExtortTypes =
        {
            CardType.Undead, CardType.Exorcist, CardType.Priest, CardType.Police,
            CardType.Guard, CardType.Witch, CardType.Arsenal, CardType.ArmedGuard,
            CardType.Minister, CardType.GrandMinister, CardType.Hero
        };

        public List<PlayerState> Players;
        /// <summary>出局顺序（先出局在前）。配合 WinnerSeat 可得完整名次：最后出局者=第二名。</summary>
        public readonly List<int> EliminationOrder = new List<int>();
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
        /// <summary>公开牌型情报（行动方, 目标, 牌型），全员可见：驱魔抽出/处决的牌、警察扣押命中的牌、亡灵挡刀。</summary>
        public event Action<int, int, CardType> OnIntel;
        /// <summary>挡刀决策事件（受害座位, 挡刀身体牌型, 被抽中的牌, 是否挡下, 受害者当前HP），供StatBlock归因统计。仅AI路径触发。</summary>
        public event Action<int, CardType, CardType, bool, int> OnGuardChoice;

        // ---- 英雄模板（未来做新英雄时改这里/做成配置）----
        public int HeroHP = 3, HeroMP = 3, HealEvery = 2, DrawEvery = 2;
        public int PriestSacStyle = 1;   // 牺牲英雄挡刀风格：1=优先牧师（2026-10-08 用户裁定定稿：1800局对照 32.3% vs style2 31.3%，且符合"牧师带亡语、挡刀比侍卫便宜"设计意图）2=优先侍卫 3=侍卫优先+EV>10才挡（实测-4.7pp弃用）
        public int[] SiegeExtraTurns;    // 攻城锤A/B：按座位覆盖额外普攻窗口长度（null=用英雄模板 ExtraAttackEarly=2；2026-10-08 攻城锤平衡实验）
        public bool[] SiegeDuelBonus;    // 攻城锤A/B候选B：按座位开启"1v1期间也有额外普攻机会"（2026-10-08 攻城锤平衡实验）
        public int[][] SiegeFixedTurns;  // 攻城锤A/B：按座位覆盖为"固定回合集合"额外普攻（null=用 ExtraAttackEarly 前N回合窗口；2026-10-14 用户实验：回合7、8）

        readonly HashSet<int> _armGuard = new HashSet<int>();  // 带刀侍卫buff生效中的座位
        readonly List<int[]> _alliances = new List<int[]>();   // [被贿赂者, 贿赂者, 剩余回合数]（按贿赂者的 StartTurn 扣减；恒1回合；美人计主动技插入其中=主目标单体禁攻）
        readonly HashSet<int> _silenced = new HashSet<int>();  // police station：下个回合技能全封（含被动）
        readonly HashSet<Card> _silencedCards = new HashSet<Card>();  // 警察：被沉默的具体牌（技能+被动全封到拥有者的回合结束）
        readonly Dictionary<int, List<Card>> _silencedCardsByOwner = new Dictionary<int, List<Card>>(); // 拥有者座位 → 被沉默的牌（到期成组解除）
        readonly HashSet<int> _freeSkills = new HashSet<int>(); // save：本回合技能免费不限次
        // 守卫"藏牌"已于 2026-10-15 改版为"无法选取"状态：牌不再离开手牌，状态记在 Card.GuardProtectTurn 上，无独立存储。
        readonly List<Hostage> _hostages = new List<Hostage>();  // 典狱长的人质：牌 + 原拥有者座位 + 归还回合（原拥有者的"第2个回合结束"归还解封）
        /// <summary>炸弹冲击波队列（2026-10-13② 改版）：爆炸=对每个敌手各1次"无来源随机攻击"（等同普攻流程，可挡刀），
        /// 队列逐个结算；人类座位弹出抉择时暂停，提交后从断点续炸。</summary>
        class BlastHit { public int Planter; public int Victim; }
        readonly List<BlastHit> _blastQueue = new List<BlastHit>();
        /// <summary>合成牌复活平衡（2026-10-08 用户设计定稿=1），按座位：0=可复活合成牌（旧规则，测试用）/ 1=禁止复活合成牌（默认）/ 2=复活后只剩1点生命。作用于所有弃牌堆回收渠道（牧师/圣骑士亡语、女巫解药、女祭司复活）。</summary>
        public int[] ReviveSynthStyle;
        /// <summary>合成奖励方案（2026-10-13 方案C对照试验 → 2026-10-14 用户裁定定稿=2）：0=奖励抽1张牌 / 2=获得【护盾】（持续X回合吸收1点伤害）。
        /// 历史：1=工人方案（2026-10-08）已废止，按0处理。</summary>
        public int[] CraftRewardStyle;
        int _nextSynthId = 10000;   // 合成牌的牌面ID（不在牌库，独立编号）
        HeroSpec[] _heroes;
        bool[] _powerUsed;
        readonly Random _rng;
        int _turnStamp;   // 回合戳：每回合自增；技能"每回合一次"用戳判定而不是布尔重置
        bool[] _earlyAttackUsed;              // 攻城锤：本回合的额外普攻机会是否已用
        int[] _apSinceHurt;                   // 医师：自受伤起消耗的行动点计数（受伤重置）
        int[] _focusSeat = new int[0];        // 强攻：连击目标（-1=无）
        int[] _focusCount = new int[0];       // 强攻：对连击目标的连续攻击次数
        int[] _focusLastTurn = new int[0];    // 强攻：连击最后一次命中的自己回合数（OwnTurnCount，回合连续性衰减判定用，2026-10-14⑦）
        int[] _focusCooldown = new int[0];    // 强攻⑧冷却（2026-10-15 用户裁定削弱）：触发追加攻击的回合戳；触发回合与下一回合的命中不计入新连击（-1=无冷却）
        int _charmGuardHolder = -1;           // 美人计魅惑（2026-10-19 最终版=被动）：魅惑生效中的持有者座位（其受攻击时转移到魅惑目标身上走完整伤害判定）
        int _charmGuardFrom = -1;             // 魅惑目标座位（大臣结盟交出侍女的对手；到持有者的下个回合开始过期）
        readonly Dictionary<int, int> _focusBonus = new Dictionary<int, int>(); // 强攻：待发放的追加攻击目标
        // ---- 新英雄状态（2026-10-14）----
        readonly int[] _lastHitBy;                             // 击杀归属：每个座位最近一次对其造成伤害的座位（凯旋判定用，-1=无）
        PlayerState _pendingElim;                              // 凯旋（人类）：淘汰收尾延迟到战利品抉择提交后
        /// <summary>开局调度券（2026-10-16 用户设计，学炉石开局换牌）：对局开始前的调度阶段，1券可将1张手牌放回牌堆底并从牌堆顶补1张。
        /// 默认规则：美人计（弱势英雄补偿）开局带3张，其余英雄0张；public 数组供测试/平衡工具覆盖。
        /// 目前仅此一种用途，后续可能扩展其它获取/使用渠道（用户明确：不是英雄专属机制）。</summary>
        public int[] MulliganTickets;
        int _mulliganSeat = -1;                                // 当前正在调度的座位（≥0=调度阶段进行中，只有该座位有行动）
        /// <summary>是否处于开局调度阶段（UI 阶段特效/操作门槛判断用，2026-10-20）。</summary>
        public bool InMulligan { get { return _mulliganSeat >= 0; } }
        /// <summary>跳过开局调度阶段（测试与脚本驱动用，2026-10-19 引入）：直接结束调度、由seat0开始第一个回合，不消耗调度券。</summary>
        public void SkipMulligan()
        {
            int g = 0;
            while (_mulliganSeat >= 0 && g++ < 100) AdvanceMulligan();
        }

        /// <summary>初始手牌张数（不含英雄牌）。3代覆写为10——法术牌入库稀释了原有牌抽率，+1补偿（2026-10-21 用户裁定）。</summary>
        protected virtual int StartHandSize { get { return 9; } }

        public GameEngine2(Dictionary<CardType, int> deckCounts, string[] names, List<IPlayerBrain2> brains, int seed, HeroSpec[] forceHeroes = null, int[] startTickets = null)
        {
            _rng = new Random(seed);
            Brains = brains;
            Players = new List<PlayerState>();
            _heroes = new HeroSpec[names.Length];
            _powerUsed = new bool[names.Length];
            _earlyAttackUsed = new bool[names.Length];
            _apSinceHurt = new int[names.Length];
            _focusSeat = new int[names.Length];
            _focusCount = new int[names.Length];
            _focusLastTurn = new int[names.Length];
            _focusCooldown = new int[names.Length];
            for (int i = 0; i < names.Length; i++) _focusCooldown[i] = -1;
            _lastHitBy = new int[names.Length];
            for (int i = 0; i < names.Length; i++) { _focusSeat[i] = -1; _lastHitBy[i] = -1; }
            ReviveSynthStyle = new int[names.Length];   // 默认全1=禁止复活合成牌（2026-10-08 用户裁定）；0=旧规则仅测试用
            for (int i = 0; i < names.Length; i++) ReviveSynthStyle[i] = 1;
            CraftRewardStyle = new int[names.Length];   // 2026-10-14 用户裁定：默认全2=方案C护盾（对照试验 1.040分/35.5% 优于抽牌 1.008/34.0%）
            for (int i = 0; i < names.Length; i++) CraftRewardStyle[i] = 2;
            for (int i = 0; i < names.Length; i++) _focusSeat[i] = -1;
            var heroPool = HeroRoster.OrderBy(_ => _rng.Next()).ToList();   // 开局随机抽英雄，互不重复
            for (int i = 0; i < names.Length; i++)
            {
                // forceHeroes（测试/脚本用）：按座位强制指定英雄，跳过随机发英雄（OrderBy 照常消耗随机流，保证同种子复现性不变）
                var spec = forceHeroes != null ? forceHeroes[i % forceHeroes.Length] : heroPool[i % heroPool.Count];
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
                for (int k = 0; k < StartHandSize && cursor < deck.Count; k++) p.Hand.Add(deck[cursor++]);
                p.Hand.Add(new Card(9000 + p.Seat, CardType.Hero));   // 英雄牌不属于牌库
            }
            SideDeck.AddRange(deck.Skip(cursor));
            Log("对局开始：牌库" + deck.Count + "张，每人" + StartHandSize + "张+英雄牌，剩余" + SideDeck.Count + "张作牌堆。");
            foreach (var p in Players)
                Log(p.Name + " 抽到了英雄【" + p.HeroName + "】（" + _heroes[p.Seat].Desc + "）。");
            // 开局调度券（2026-10-16/19/20）：默认美人计、致命一击、冷酷追猎各3张（弱势英雄补偿性资源），攻城锤2张、猎巫行动1张（2026-10-19 22:27 用户裁定），startTickets 可覆盖
            MulliganTickets = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
                MulliganTickets[i] = startTickets != null ? startTickets[i] :
                    (_heroes[i].Id == 7 || _heroes[i].Id == 9 || _heroes[i].Id == 10 ? 3 :
                     _heroes[i].Id == 3 ? 2 :
                     _heroes[i].Id == 5 ? 1 : 0);
            for (int s = 0; s < Players.Count; s++)
                if (MulliganTickets[s] > 0) { _mulliganSeat = s; Current = s; break; }
            if (_mulliganSeat < 0) StartTurn(0);
            else Log("开局调度阶段：" + Players[_mulliganSeat].Name + " 持有【调度券】×" + MulliganTickets[_mulliganSeat]
                + "，可将至多 " + MulliganTickets[_mulliganSeat] + " 张手牌与牌堆顶的牌交换（1券=1张，换哪张不公开）。");
        }

        /// <summary>开局调度阶段推进：下一个持券座位接管 Current；无人持券则结束调度、seat0 的第一个回合正式开始。</summary>
        void AdvanceMulligan()
        {
            for (int s = _mulliganSeat + 1; s < Players.Count; s++)
                if (MulliganTickets[s] > 0) { _mulliganSeat = s; Current = s; return; }
            _mulliganSeat = -1;
            Log("开局调度阶段结束，对局正式开始。");
            Current = 0;
            StartTurn(0);
        }

        protected void Log(string msg) { History.Add(msg); if (OnLog != null) OnLog(msg); }
        /// <summary>外部（GameBootstrap2）向操作历史写入播报，如 AI 开局策略公布（2026-10-12 策略随机）。</summary>
        public void LogEvent(string msg) { Log(msg); }
        void RaiseHostile(int attacker, int victim, ActionKind kind) { if (OnHostile != null) OnHostile(attacker, victim, kind); }

        /// <summary>卡牌守恒（2代标准47张牌库：44张+工人×3，每人1张英雄牌）。
        /// 合成牌（SynthHP≥0，id≥10000）是局内凭空造出的第45+张牌，不属于物理牌库，不计入守恒。</summary>
        public int TotalCards { get { return Players.Sum(p => p.Hand.Count(c => c.SynthHP < 0)) + Discard.Count(c => c.SynthHP < 0) + SideDeck.Count; } }

        /// <summary>典狱长的人质：被扣押的牌与其原拥有者（归还/解封计时依据）。</summary>
        class Hostage { public Card Card; public int OwnerSeat; public int ReleaseTurn; }   // ReleaseTurn = 原拥有者 OwnTurnCount ≥ 此值的那个回合结束归还（2026-10-14：扣押2回合冷却/人质扣2回合）

        /// <summary>seat 合成 synth 实际需要消耗的手牌：固定配方按类型各取1张（"任意"位指定的牌优先让位给固定槽，避免一张牌被算两次）；
        /// "任意"位取 wildId 指定的牌（玩家手动选择，必须是自己手牌里的非英雄牌），wildId≤0 时退回第一张未被选用的非英雄牌。
        /// 配方凑不齐返回 null。</summary>
        public List<Card> CraftCost(int seat, CardType synth, int wildId = 0)
        {
            CardType?[] rec;
            if (!CraftRecipes.TryGetValue(synth, out rec)) return null;
            var me = Players[seat];
            Card wild = wildId > 0 ? me.Hand.FirstOrDefault(x => x.Id == wildId) : null;
            if (wildId > 0 && (wild == null || wild.Type == CardType.Hero || wild.GuardProtectTurn >= 0)) return null;   // 指定的"任意"牌必须是自己手牌中可被选取的非英雄牌
            var cost = new List<Card>();
            foreach (var rt in rec)
            {
                Card c;
                if (rt.HasValue) c = me.Hand.FirstOrDefault(x => x.Type == rt.Value && !cost.Contains(x) && x != wild && x.GuardProtectTurn < 0);
                else if (wild != null) c = wild;
                else c = me.Hand.FirstOrDefault(x => x.Type != CardType.Hero && !cost.Contains(x) && x.GuardProtectTurn < 0);
                if (c == null) return null;
                cost.Add(c);
            }
            return cost;
        }

        /// <summary>警察的牌沉默：该牌（技能+被动）封到拥有者的回合结束。</summary>
        public bool CardSilenced(Card c) { return _silencedCards.Contains(c); }

        /// <summary>该玩家手牌中是否有"被警察封印的某类型牌"（公开信息：封印时播报过牌型）。AI 攻击优先级用。</summary>
        public bool HasSealed(int seat, CardType t)
        {
            return seat >= 0 && seat < Players.Count
                && Players[seat].Hand.Any(c => c.Type == t && _silencedCards.Contains(c));
        }

        /// <summary>结盟保护：attackerSeat 是否被禁止攻击 victimSeat。</summary>
        public bool IsProtected(int attackerSeat, int victimSeat)
        {
            return _alliances.Any(a => a[0] == attackerSeat && a[1] == victimSeat);
        }

        /// <summary>2026-10-14⑥：该牌是否为典狱长手中的在押人质（扣押物会强制归还原拥有者，对持有者价值=0，AI 贿赂估值用）。</summary>
        public bool IsHostage(Card c) { return _hostages.Any(h => h.Card == c); }

        /// <summary>带刀侍卫亡语反击是否对该座位生效：现行=已发动的1回合布防 buff（_armGuard）；
        /// 光环实验（ArmGuardAuraAll）=持有者手牌中有带刀侍卫即常驻。AI 挡刀决策也读这个。</summary>
        public bool HasArmGuard(int seat)
        {
            return _armGuard.Contains(seat)
                || (ArmGuardAuraAll && Players[seat].Alive && Players[seat].Hand.Any(c => c.Type == CardType.ArmedGuard));
        }

        /// <summary>带刀侍卫光环（2026-10-15 用户裁定转正，默认 true）：true=全场带刀侍卫为光环——
        /// 持有者手牌中有带刀侍卫即常驻"侍卫/大臣/带刀侍卫/守卫亡语反击"（无需发动、不耗蓝、持续到牌离手），
        /// 替代旧主动技（1蓝布防1回合）。false=回退旧主动技（历史实验对照用，统计工具 StatArmGuard 用）。
        /// 口径：只在牌于手牌中时生效（被守卫护住/被典狱长扣押会暂时失效——护住的牌移出选取但不离手仍算在手，
        /// 扣押会真正离手）；被沉默整套时反击被挡；抽中路径不查沉默（沿用旧口径）。</summary>
        public bool ArmGuardAuraAll = true;

        /// <summary>save英雄技是否生效中：本回合技能使用后不进冷却（回合技可连发，全局限定技也不消耗）。</summary>
        public bool SkillsFree(int seat) { return _freeSkills.Contains(seat); }

        /// <summary>该牌本回合是否已用过技能（回合戳判定，与牌当前在不在手无关）。UI 显示与测试断言都用这个。</summary>
        public bool UsedThisTurn(Card c) { return c.UsedSkillTurn == _turnStamp; }

        public bool IsSilenced(int seat) { return _silenced.Contains(seat); }

        /// <summary>该牌是否处于守卫的"无法选取"状态（2026-10-15 改版）：牌留在手牌，但不参与一切针对手牌的判定。</summary>
        public bool IsGuardProtected(Card c) { return c.GuardProtectTurn >= 0; }

        /// <summary>该座位手牌中处于"无法选取"状态的牌数。守卫发动是公开行动（但护住的是哪张牌只有拥有者知道），
        /// 张数本身全场可见——UI 状态栏与 AI 威胁评估用。</summary>
        public int GuardProtectedCount(int seat) { return seat >= 0 && seat < Players.Count ? Players[seat].Hand.Count(c => c.GuardProtectTurn >= 0) : 0; }
        public HeroSpec HeroOf(int seat) { return _heroes[seat]; }

        /// <summary>seat 贿赂了谁（结盟对象座位，无则-1），UI显示用。</summary>
        public int BribedBy(int briberSeat)
        {
            var a = _alliances.FirstOrDefault(x => x[1] == briberSeat);
            return a == null ? -1 : a[0];
        }

        protected virtual void StartTurn(int seat)
        {
            var p = Players[seat];
            p.OwnTurnCount++;
            // ⑦ 强攻削弱（2026-10-14 用户裁定）：连击必须每回合持续命中同一目标——自己回合开始时若上个回合没打到连击目标
            // （停手、被封印/结盟卡住、转火他人），连击清零。打其它目标本就会换目标重置（TrackFocus），这里补"漏回合"的衰减。
            if (_heroes[seat].FocusFire && _focusCount[seat] > 0 && _focusLastTurn[seat] < p.OwnTurnCount - 1)
            {
                Log(p.Name + " 的【强攻】连击中断：上个回合没有继续攻击 " + Players[_focusSeat[seat]].Name + "。（连击需每回合持续命中同一目标）");
                _focusSeat[seat] = -1;
                _focusCount[seat] = 0;
            }
            // 方案C护盾到期：自己的回合开始走表（与青苹果倒数同一节拍）
            if (p.ShieldCharges > 0 && p.OwnTurnCount > p.ShieldExpireOwnTurn)
            {
                p.ShieldCharges = 0;
                Log(p.Name + " 的【护盾】到期消散。");
            }
            p.LastTurnStamp = _turnStamp;                    // 记录上一个回合开始戳（解药复活窗口用）
            _turnStamp++;                                    // 回合戳自增：所有牌的"已用"状态随之过期，与牌在不在手无关
            var h = _heroes[seat];
            // 工人（2026-10-13 进2代牌库）：手牌中有工人 → 行动点上限+1（多张不叠加，只算一次）
            int workerBonus = p.Hand.Any(c => c.Type == CardType.Worker) ? 1 : 0;
            p.MaxMP = h.MP + (h.EarlyMpBonus > 0 && p.OwnTurnCount <= h.ExtraAttackEarly ? h.EarlyMpBonus : 0) + workerBonus;   // 攻城锤：前2回合行动点+1（2026-10-09 用户裁定）
            p.MP = p.MaxMP;
            _armGuard.Remove(seat);                          // 自己的带刀侍卫buff到自己的下回合开始过期
            // 我贿赂来的保护按"我的回合开始"扣减：剩余回合数-1，归零过期（2026-10-15③：多回合结盟移除，恒1回合；美人计1回合光环同理）
            for (int i = _alliances.Count - 1; i >= 0; i--)
                if (_alliances[i][1] == seat && --_alliances[i][2] <= 0) _alliances.RemoveAt(i);
            // 美人计魅惑：到持有者自己的下个回合开始过期（持续1回合）——攻击重定向随之失效
            if (_charmGuardHolder == seat) { _charmGuardHolder = -1; _charmGuardFrom = -1; }
            // 守卫"无法选取"状态到期：自己的回合开始时清除（2026-10-15 改版——牌一直留在手牌里，只清状态戳）
            foreach (var c in p.Hand.Where(c => c.GuardProtectTurn >= 0 && c.GuardProtectTurn < p.OwnTurnCount).ToList())
            {
                c.GuardProtectTurn = -1;
                Log(p.Name + " 的【守卫】护佑到期：1张牌重新可以被选取。");
            }
            // 医师天赋改为"消耗行动点回血"（见 NoteApSpent），回合开始不再自动回血
            if (p.OwnTurnCount % DrawEvery == 0 && SideDeck.Count > 0)
            {
                var c = SideDeck[0]; SideDeck.RemoveAt(0); p.Hand.Add(c);
                Log(p.Name + " 从牌堆补充了1张牌。（牌堆剩余" + SideDeck.Count + "）");
            }
            BasicAttackUsed = false;
            _earlyAttackUsed[seat] = false;                  // 攻城锤：额外普攻机会每回合刷新（只在前2回合可被使用）
            // 青苹果：受伤倒数只在拥有者自己的回合开始时走表（再次受伤不打断、由 NoteAppleCountdown 保证不重置）
            foreach (var apple in p.Hand.Where(c => c.Type == CardType.GreenApple && c.AppleTimer > 0).ToList())
            {
                apple.AppleTimer--;
                if (apple.AppleTimer == 0)
                {
                    p.Hand.Remove(apple);
                    ToDiscard(apple);
                    if (p.HP < p.MaxHP) p.HP++;
                    Log(p.Name + " 的【青苹果】熟透了，自动吃掉：回复1点HP（" + p.HP + "/" + p.MaxHP + "）。");
                }
                else Log(p.Name + " 的【青苹果】还在倒数（剩 " + apple.AppleTimer + " 回合）。");
            }
            // 女祭司·祈祷到期：2回合后把自身1点生命转化为英雄的1点生命（生命耗尽则女祭司进入弃牌堆）
            foreach (var ps in p.Hand.Where(c => c.Type == CardType.Priestess && c.PrayerDueTurn > 0 && p.OwnTurnCount >= c.PrayerDueTurn).ToList())
            {
                ps.PrayerDueTurn = -1;
                ps.SynthHP--;
                if (p.HP < p.MaxHP) p.HP++;
                if (ps.SynthHP <= 0)
                {
                    p.Hand.Remove(ps);
                    ToDiscard(ps);
                    Log(p.Name + " 的【女祭司】祈祷应验：1点生命化为英雄的1点生命（" + p.HP + "/" + p.MaxHP + "），女祭司圣力耗尽，随风而逝。");
                }
                else Log(p.Name + " 的【女祭司】祈祷应验：1点生命化为英雄的1点生命（" + p.HP + "/" + p.MaxHP + "），剩余 " + ps.SynthHP + " 点生命。");
            }
            // 恐怖分子炸弹：触发器绑定在安放者身上——自己的回合开始结算到期的炸弹（检查恐怖分子还活不活）
            foreach (var bomb in AllCards().Where(c => c.BombActive && c.BombPlanter == seat
                                                       && p.OwnTurnCount >= c.BombDueTurn).ToList())
                ResolveBomb(bomb);
            // 技能"已用"判定已改为回合戳（UsedSkillTurn == _turnStamp），无需再逐张重置布尔位；
            // 这样被警察扣押/守卫藏起、本回合不在手牌里的牌也不会漏掉刷新。
        }

        // ------------------------------------------------------------------
        // 合法行动
        // ------------------------------------------------------------------

        public virtual List<GameAction> GetLegalActions(int seat)
        {
            var list = new List<GameAction>();
            // 开局调度阶段：只有当前调度座位有行动（换牌×余券 / 结束调度）
            if (_mulliganSeat >= 0)
            {
                if (GameOver || seat != _mulliganSeat) return list;
                var mp = Players[seat];
                if (MulliganTickets[seat] > 0)
                    foreach (var c in mp.Hand.Where(c => c.Type != CardType.Hero))
                        list.Add(new GameAction { Kind = ActionKind.Mulligan, SourceCardId = c.Id });
                list.Add(new GameAction { Kind = ActionKind.EndMulligan });
                return list;
            }
            if (Pending != null || GameOver || seat != Current || !Players[seat].Alive) return list;
            var me = Players[seat];
            var targets = Players.Where(p => p.Alive && p.Seat != seat && p.Hand.Count > 0).ToList();
            bool silenced = _silenced.Contains(seat);
            bool free = _freeSkills.Contains(seat);

            if ((!BasicAttackUsed || CanEarlyAttack(seat)) && me.MP >= COST_BASIC_ATTACK)
                foreach (var t in targets)
                    if (!IsProtected(seat, t.Seat))
                        list.Add(new GameAction { Kind = ActionKind.Attack, TargetSeat = t.Seat });

            if (!silenced)
            foreach (var c in me.Hand)
            {
                if (c.Type == CardType.Hero || (c.UsedSkillTurn == _turnStamp && !free) || _silencedCards.Contains(c)) continue;
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
                        if ((!c.PoisonUsed || free) && me.MP >= COST_POISON)   // save期间：全局限定技也不消耗
                            foreach (var t in targets)
                                if (!IsProtected(seat, t.Seat))
                                    list.Add(new GameAction { Kind = ActionKind.PoisonAttack, TargetSeat = t.Seat, SourceCardId = c.Id });
                        if ((!c.AntidoteUsed || free) && me.MP >= COST_ANTIDOTE
                            && Discard.Any(d => d.Type.IsRole() && d.Type != CardType.Hero && d.DiedStamp >= me.LastTurnStamp))   // 只能救上一回合内阵亡的
                            list.Add(new GameAction { Kind = ActionKind.Antidote, SourceCardId = c.Id });
                        break;
                    case CardType.Arsenal:
                        if (me.MP >= COST_ARSENAL)
                            foreach (var t in targets)
                                if (!IsProtected(seat, t.Seat))
                                    list.Add(new GameAction { Kind = ActionKind.ArsenalAttack, TargetSeat = t.Seat, SourceCardId = c.Id });
                        break;
                    case CardType.Minister:
                        // 2026-10-14④：每回合限贿赂1次（按座位，不论几张大臣）——纯EV判定下AI会把3点蓝全用来贿赂，
                        // 全场互相买停战导致攻击全被 IsProtected 封死、对局瘫痪（实测200局10.6万次贿赂/20000步打不完）。
                        if (me.MP >= COST_BRIBE && me.LastBribeTurn != me.OwnTurnCount)
                        {
                            var giveable = me.Hand.Where(x => x.Type != CardType.Hero && x.Id != c.Id && x.GuardProtectTurn < 0).ToList();
                            foreach (var t in Players.Where(p => p.Alive && p.Seat != seat))
                            {
                                if (IsProtected(t.Seat, seat)) continue;   // 结盟生效中不能重复贿赂（防止互相续命的死循环）
                                if (IsProtected(seat, t.Seat)) continue;   // 2026-10-14⑤：对方盟约保护我中（我不能攻击对方）→ 禁回贿，
                                                                           // 防1v1互贿死循环（C贿B后B不能打C，B若回贿C则双向冻结永不终结）。
                                foreach (var g in giveable)
                                    list.Add(new GameAction { Kind = ActionKind.Bribe, TargetSeat = t.Seat, SourceCardId = c.Id, SecondCardId = g.Id });
                            }
                        }
                        break;
                    case CardType.GrandMinister:
                        if (me.MP >= COST_EXTORT && (!c.ExtortUsed || free))   // save期间：限定技也不消耗
                            foreach (var tt in ExtortTypes)
                                list.Add(new GameAction { Kind = ActionKind.Extort, SourceCardId = c.Id, TargetType = tt });
                        break;
                    case CardType.ArmedGuard:
                        if (!ArmGuardAuraAll && me.MP >= COST_ARMGUARD && !_armGuard.Contains(seat))
                            list.Add(new GameAction { Kind = ActionKind.ArmGuard, SourceCardId = c.Id });
                        break;
                    case CardType.Warden:
                        if (me.MP >= COST_HIDE)
                            foreach (var h in me.Hand)
                            {
                                if (h.Type == CardType.Hero || h.Id == c.Id) continue;   // 不能护英雄/护守卫自己（2026-10-15 用户裁定恢复）
                                if (h.LastGuardedTurn == me.OwnTurnCount - 1) continue;  // 防连守：不能连续两个回合护同一张（状态到期≠守过记忆清除）
                                if (h.GuardProtectTurn >= 0) continue;                   // 已在"无法选取"状态，重复施加无意义
                                list.Add(new GameAction { Kind = ActionKind.Hide, SourceCardId = c.Id, SecondCardId = h.Id });
                            }
                        break;
                    case CardType.Priestess:
                        if ((!c.ReviveUsed || free) && me.MP >= COST_SYNTH_SKILL
                            && Discard.Any(d => d.Type.IsRole() && d.Type != CardType.Hero))
                            list.Add(new GameAction { Kind = ActionKind.SynthRevive, SourceCardId = c.Id });
                        if ((!c.PrayerUsed || free) && me.MP >= COST_SYNTH_SKILL && c.SynthHP > 0)
                            list.Add(new GameAction { Kind = ActionKind.SynthPrayer, SourceCardId = c.Id });
                        break;
                    case CardType.Jailer:
                        // 2回合冷却（2026-10-14 用户裁定，削弱停战软锁）：距上次扣押不足2个自己的回合不可再扣押；save期间不消耗冷却
                        if (me.MP >= COST_SYNTH_SKILL && (free || c.SeizeStamp < 0 || me.OwnTurnCount - c.SeizeStamp >= 2))
                            foreach (var t in targets)
                                if (t.Hand.Any(x => x.Type != CardType.Hero && x.GuardProtectTurn < 0))
                                    list.Add(new GameAction { Kind = ActionKind.SynthHostage, TargetSeat = t.Seat, SourceCardId = c.Id });
                        break;
                    case CardType.Inquisitor:
                        if (me.MP >= COST_SYNTH_SKILL)
                            foreach (var t in targets)
                                list.Add(new GameAction { Kind = ActionKind.SynthJudge, TargetSeat = t.Seat, SourceCardId = c.Id });
                        break;
                    case CardType.Terrorist:
                        // 安放炸弹（2026-10-13 改版）：一张恐怖分子同时只能有一枚炸弹在倒数
                        if (!c.BombActive && me.MP >= COST_BOMB)
                            list.Add(new GameAction { Kind = ActionKind.BombPlant, SourceCardId = c.Id });
                        break;
                }
            }
            // 合成（2026-10-06）：配方在手 + 1行动点。固定配方直接给1个行动；
            // 含"任意"位的配方：每张可用的第三张牌各生成1个行动，由玩家/AI手动指定消耗哪张（2026-10-07）。
            if (!silenced && me.MP >= COST_CRAFT)
                foreach (var kv in CraftRecipes)
                {
                    if (!kv.Value.Contains(null))
                    {
                        if (CraftCost(seat, kv.Key) != null)
                            list.Add(new GameAction { Kind = ActionKind.Craft, TargetType = kv.Key });
                    }
                    else
                    {
                        foreach (var cand in me.Hand.Where(x => x.Type != CardType.Hero && CraftCost(seat, kv.Key, x.Id) != null))
                            list.Add(new GameAction { Kind = ActionKind.Craft, TargetType = kv.Key, SourceCardId = cand.Id });
                    }
                }
            // 美人计已改为被动（2026-10-19 最终版）：无主动技，HeroPower 仅 police station / save。
            if (!silenced && _heroes[seat].HasPower && !_powerUsed[seat])
                list.Add(new GameAction { Kind = ActionKind.HeroPower });
            list.Add(new GameAction { Kind = ActionKind.EndTurn });
            return list;
        }

        // ------------------------------------------------------------------
        // 行动结算
        // ------------------------------------------------------------------

        public virtual void ApplyAction(GameAction a)
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
            // 开局调度阶段：换牌=该牌放回牌堆底、牌堆顶补1张（消耗1券；不耗行动点；换掉哪张不公开）
            if (_mulliganSeat >= 0)
            {
                int mseat = _mulliganSeat;
                var mp = Players[mseat];
                if (a.Kind == ActionKind.Mulligan && MulliganTickets[mseat] > 0)
                {
                    var card = mp.Hand.FirstOrDefault(c => c.Id == a.SourceCardId && c.Type != CardType.Hero);
                    if (card == null) return;
                    MulliganTickets[mseat]--;
                    mp.Hand.Remove(card);
                    SideDeck.Add(card);
                    Card drew = null;
                    if (SideDeck.Count > 0) { drew = SideDeck[0]; SideDeck.RemoveAt(0); mp.Hand.Add(drew); }
                    Log(mp.Name + " 使用1张【调度券】替换了1张手牌（剩余调度券 " + MulliganTickets[mseat]
                        + (drew != null ? "，补入1张" : "，牌堆已空未能补牌") + "，牌堆剩余 " + SideDeck.Count + "）。");
                    if (MulliganTickets[mseat] == 0) AdvanceMulligan();
                    return;
                }
                if (a.Kind == ActionKind.EndMulligan) { AdvanceMulligan(); return; }
                return;   // 调度阶段其它行动一律不理
            }
            if (GameOver || Pending != null) return;
            var me = Players[Current];
            if (!me.Alive) return;
            int seat = Current;
            bool free = _freeSkills.Contains(seat);
            if (_silenced.Contains(seat) && a.Kind != ActionKind.Attack && a.Kind != ActionKind.EndTurn) return;

            switch (a.Kind)
            {
                case ActionKind.Attack:
                    if (!ValidTarget(seat, a.TargetSeat) || IsProtected(seat, a.TargetSeat)) return;
                    if (BasicAttackUsed && !CanEarlyAttack(seat)) return;   // 攻城锤：前2回合的额外普攻机会
                    if (me.MP < COST_BASIC_ATTACK) return;                  // 普攻耗1行动点
                    me.MP -= COST_BASIC_ATTACK;
                    NoteApSpent(me);
                    if (BasicAttackUsed) _earlyAttackUsed[seat] = true;
                    else BasicAttackUsed = true;
                    DoAttack(seat, a.TargetSeat, a.Kind);
                    TryFireFocusBonus(seat);
                    break;
                case ActionKind.ArsenalAttack:
                case ActionKind.PoisonAttack:
                    {
                        if (!ValidTarget(seat, a.TargetSeat) || IsProtected(seat, a.TargetSeat)) return;
                        int cost = a.Kind == ActionKind.PoisonAttack ? COST_POISON : COST_ARSENAL;
                        CardType expect = a.Kind == ActionKind.PoisonAttack ? CardType.Witch : CardType.Arsenal;
                        var probe = me.Hand.FirstOrDefault(c => c.Id == a.SourceCardId && c.Type == expect);
                        if (probe == null || (a.Kind == ActionKind.PoisonAttack && probe.PoisonUsed && !free)) return;
                        var card = PaySkill(me, a.SourceCardId, cost, expect, free);
                        if (card == null) return;
                        if (a.Kind == ActionKind.PoisonAttack && !free) card.PoisonUsed = true;   // save期间：不进冷却（整局限定不消耗）
                        DoAttack(seat, a.TargetSeat, a.Kind);
                        TryFireFocusBonus(seat);
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
                        // 先查有没有可救的（上一回合内阵亡），没有就直接空过——不能白扣蓝
                        var options = DiscardOptions(seat, includePriest: true, freshSince: me.LastTurnStamp);
                        if (options.Count == 0) return;
                        var card = PaySkill(me, a.SourceCardId, COST_ANTIDOTE, CardType.Witch, free);
                        if (card == null || (card.AntidoteUsed && !free)) return;
                        if (!free) card.AntidoteUsed = true;   // save期间：不进冷却
                        Log(me.Name + " 使用女巫·解药：救活了一张上一回合内阵亡的角色牌。");
                        OfferDiscardPick(me, options, "女巫·解药");
                        break;
                    }
                case ActionKind.Bribe:
                    {
                        var card = PaySkill(me, a.SourceCardId, COST_BRIBE, CardType.Minister, free);
                        if (card == null) return;
                        me.LastBribeTurn = me.OwnTurnCount;   // 2026-10-14④ 每回合限1次（按座位）
                        ResolveBribe(seat, a.TargetSeat, a.SecondCardId);
                        break;
                    }
                case ActionKind.Extort:
                    {
                        var card = PaySkill(me, a.SourceCardId, COST_EXTORT, CardType.GrandMinister, free);
                        if (card == null || (card.ExtortUsed && !free)) return;   // 限定技：整局一次（save期间不消耗）
                        if (!free) card.ExtortUsed = true;
                        ResolveExtort(seat, a.TargetType);
                        break;
                    }
                case ActionKind.ArmGuard:
                    {
                        if (ArmGuardAuraAll) return;   // 光环实验：布防常驻，主动技不再存在
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
                        var hide = me.Hand.FirstOrDefault(c => c.Id == a.SecondCardId && c.Type != CardType.Hero);
                        if (hide == null || hide.Id == card.Id || hide.LastGuardedTurn == me.OwnTurnCount - 1
                            || hide.GuardProtectTurn >= 0) return;
                        hide.GuardProtectTurn = me.OwnTurnCount;
                        hide.LastGuardedTurn = me.OwnTurnCount;
                        Log(me.Name + " 用【守卫】给手牌中的1张牌施加了【无法选取】：直到 " + me.Name + " 的下个回合开始，"
                            + "这张牌留在手牌，但不会被攻击抽中/挡刀/驱魔/审判/扣押/勒索/贿赂/合成等任何针对手牌的判定碰到。（护住的是哪张牌不公开）");
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
                                // 2026-10-13：被沉默者名下倒数中的炸弹全部拆除（返还cd）
                                foreach (var bomb in AllCards().Where(c => c.BombActive && c.BombPlanter == o.Seat).ToList())
                                    DefuseBomb(bomb, "被 police station 沉默");
                            }
                            Log(me.Name + " 发动英雄技【police station】：所有敌人的下个回合什么技能都用不了（含被动）！");
                        }
                        else if (spec.Id == 2)
                        {
                            _freeSkills.Add(seat);
                            Log(me.Name + " 发动英雄技【save】：本回合技能使用后不进冷却——回合技可连发，女巫双药/军机大臣限定技用掉也不消耗（蓝耗照付）！");
                        }
                        break;
                    }
                case ActionKind.EndTurn:
                    EndTurn();
                    break;
                case ActionKind.Craft:
                    {
                        // 合成：1行动点 + 配方3张牌 → 一张2点生命的合成牌 + 奖励（2026-10-14 定稿=方案C护盾；
                        // CraftRewardStyle[seat]==0 时回退为奖励抽1张牌；历史"奖励工人"方案已废止——工人是牌库常驻牌×3）
                        if (me.MP < COST_CRAFT) return;
                        var cost = CraftCost(seat, a.TargetType, a.SourceCardId);
                        if (cost == null) return;
                        me.MP -= COST_CRAFT;
                        NoteApSpent(me);
                        foreach (var c in cost) { me.Hand.Remove(c); ToDiscard(c); }
                        var synth = new Card(_nextSynthId++, a.TargetType) { SynthHP = SYNTH_HP };
                        me.Hand.Add(synth);
                        string drew = "";
                        if (CraftRewardStyle[seat] == 2)
                        {
                            // 方案C（2026-10-13 用户设计）：护盾持续X回合，吸收1点伤害——保护本体（HP）与整个手牌（攻击被整个吞掉，不抽牌）
                            me.ShieldCharges = CRAFT_SHIELD_CHARGES;
                            me.ShieldExpireOwnTurn = me.OwnTurnCount + CRAFT_SHIELD_TURNS;
                            drew = "，获得【护盾】：持续 " + CRAFT_SHIELD_TURNS + " 回合，可吸收 " + CRAFT_SHIELD_CHARGES + " 点伤害（保护本体与整个手牌）";
                        }
                        else if (SideDeck.Count > 0)
                        {
                            var rc = SideDeck[0]; SideDeck.RemoveAt(0); me.Hand.Add(rc);
                            drew = "，奖励抽了1张牌";
                        }
                        else drew = "（牌堆已空，无奖励牌）";
                        Log(me.Name + " 消耗1行动点，将 " + string.Join("＋", cost.Select(c => "【" + c.Type.Name() + "】"))
                            + " 合成了【" + a.TargetType.Name() + "】（" + SYNTH_HP + "点生命）" + drew + "！");
                        break;
                    }
                case ActionKind.SynthRevive:
                    {
                        // 女祭司·复活（限定技）：从弃牌堆复活1张角色牌（不限阵亡时间，区别于女巫解药）
                        var options = DiscardOptions(seat, includePriest: true);
                        if (options.Count == 0) return;
                        var card = PaySkill(me, a.SourceCardId, COST_SYNTH_SKILL, CardType.Priestess, free);
                        if (card == null || (card.ReviveUsed && !free)) return;
                        if (!free) card.ReviveUsed = true;   // save期间：不进冷却（整局限定不消耗）
                        Log(me.Name + " 发动【女祭司·复活】：从弃牌堆复活1张角色牌。");
                        OfferDiscardPick(me, options, "女祭司·复活");
                        break;
                    }
                case ActionKind.SynthPrayer:
                    {
                        // 女祭司·祈祷（限定技）：2回合后（自己的第2个回合开始）自身1点生命转化为英雄1点生命
                        var card = PaySkill(me, a.SourceCardId, COST_SYNTH_SKILL, CardType.Priestess, free);
                        if (card == null || (card.PrayerUsed && !free) || card.SynthHP <= 0) return;
                        if (!free) card.PrayerUsed = true;
                        card.PrayerDueTurn = me.OwnTurnCount + 2;
                        Log(me.Name + " 发动【女祭司·祈祷】：2回合后，女祭司将把1点自身生命转化为英雄的1点生命。");
                        break;
                    }
                case ActionKind.SynthHostage:
                    {
                        // 典狱长·扣押（2回合冷却，2026-10-14 用户裁定削弱停战软锁）：对方1张随机非英雄牌来作人质并封印，
                        // 人质扣2回合——原拥有者的第2个回合结束归还解封
                        if (!ValidTarget(seat, a.TargetSeat)) return;
                        var card = PaySkill(me, a.SourceCardId, COST_SYNTH_SKILL, CardType.Jailer, free);
                        if (card == null) return;
                        card.SeizeStamp = me.OwnTurnCount;
                        RaiseHostile(seat, a.TargetSeat, a.Kind);
                        ResolveHostage(seat, a.TargetSeat);
                        break;
                    }
                case ActionKind.SynthJudge:
                    {
                        // 审判官·审判：随机揭示对手1张牌，亡灵/女巫（异端）则处决
                        if (!ValidTarget(seat, a.TargetSeat)) return;
                        var card = PaySkill(me, a.SourceCardId, COST_SYNTH_SKILL, CardType.Inquisitor, free);
                        if (card == null) return;
                        RaiseHostile(seat, a.TargetSeat, a.Kind);
                        ResolveJudge(seat, a.TargetSeat);
                        break;
                    }
                case ActionKind.BombPlant:
                    {
                        // 恐怖分子·安放炸弹（2026-10-13 用户改版）：2回合后爆炸，对每个敌手各发动1次无来源随机攻击（等同普攻，
                        // 随机抽1张手牌正常结算，不记仇），恐怖分子自己同归于尽。触发器绑定在安放者身上（到安放者的回合检查恐怖分子还活不活）。
                        var card = PaySkill(me, a.SourceCardId, COST_BOMB, CardType.Terrorist, free);
                        if (card == null || card.BombActive) return;
                        card.BombActive = true;
                        card.BombPlanter = seat;
                        card.BombDueTurn = me.OwnTurnCount + 2;
                        Log(me.Name + " 的【恐怖分子】安放了一枚炸弹：2回合后爆炸，对每个敌手各发动1次无来源随机攻击——等同普攻，随机抽1张手牌正常结算（恐怖分子自己也将同归于尽）！");
                        break;
                    }
            }
        }

        Card PaySkill(PlayerState me, int cardId, int cost, CardType expect, bool free = false)
        {
            var card = me.Hand.FirstOrDefault(c => c.Id == cardId && c.Type == expect);
            if (card == null) return null;
            if (!free && card.UsedSkillTurn == _turnStamp) return null;   // save期间才允许同卡连发
            if (me.MP < cost) return null;                       // 蓝耗照付
            me.MP -= cost;
            NoteApSpent(me);
            card.UsedSkillTurn = _turnStamp;
            return card;
        }

        /// <summary>医师天赋：自受伤起，每消耗6点行动点回复1点HP（再次受伤时计数重置）。满血不触发但计数保留。</summary>
        void NoteApSpent(PlayerState me)
        {
            if (!_heroes[me.Seat].HealTalent) return;
            _apSinceHurt[me.Seat]++;
            while (_apSinceHurt[me.Seat] >= 6 && me.HP < me.MaxHP)
            {
                _apSinceHurt[me.Seat] -= 6;
                me.HP++;
                Log(me.Name + " 的天赋生效：消耗6点行动点，回复1点HP（" + me.HP + "/" + me.MaxHP + "）。");
            }
        }

        /// <summary>青苹果：英雄受伤后，其手牌中所有没在倒数的青苹果进入2回合倒数（再次受伤不会打断/重置已存在的倒数）。</summary>
        void NoteAppleCountdown(PlayerState hurt)
        {
            bool any = false;
            foreach (var c in hurt.Hand)
                if (c.Type == CardType.GreenApple && c.AppleTimer < 0) { c.AppleTimer = 2; any = true; }
            if (any) Log(hurt.Name + " 的【青苹果】开始2回合倒数（再次受伤不会打断）。");
        }

        bool ValidTarget(int attackerSeat, int targetSeat)
        {
            if (targetSeat < 0 || targetSeat >= Players.Count || targetSeat == attackerSeat) return false;
            var t = Players[targetSeat];
            return t.Alive && t.Hand.Count > 0;
        }

        /// <summary>攻城锤：2026-10-20 起默认=英雄升级后（升级当回合与下个自己的回合）各1次额外普攻机会（HeroSpec.SiegeOnLevelUp，2026-10-19 22:27 用户裁定取代固定7/8回合）。
        /// 实验旋钮优先级：SiegeFixedTurns（按座位固定回合集合）> SiegeExtraTurns（按座位覆盖窗口长度，2026-10-08 语义）
        /// > ExtraAttackTurns（英雄模板固定回合）> SiegeOnLevelUp（升级后2回合窗口）> ExtraAttackEarly（前N回合窗口，旧语义）。
        /// SiegeDuelBonus 非 null 时：该座位在 1v1（仅2人存活）期间也拥有额外普攻机会（A/B 候选B"残局发动机"）。</summary>
        bool CanEarlyAttack(int seat)
        {
            var h = _heroes[seat];
            bool window;
            if (SiegeFixedTurns != null && SiegeFixedTurns[seat] != null)
                window = Array.IndexOf(SiegeFixedTurns[seat], Players[seat].OwnTurnCount) >= 0;
            else if (SiegeExtraTurns != null)
            {
                if (h.ExtraAttackEarly <= 0) return false;   // 与 2026-10-08 实验语义一致：窗口长度覆盖只影响攻城锤座位
                window = Players[seat].OwnTurnCount <= SiegeExtraTurns[seat];
            }
            else if (h.ExtraAttackTurns != null)
                window = Array.IndexOf(h.ExtraAttackTurns, Players[seat].OwnTurnCount) >= 0;
            else if (h.SiegeOnLevelUp)
            {
                var sp = Players[seat];
                window = sp.HeroLevel >= 1 && sp.OwnTurnCount <= sp.LevelTurn + 1;   // 升级当回合+下个自己回合=2回合窗口
            }
            else
            {
                if (h.ExtraAttackEarly <= 0) return false;
                window = Players[seat].OwnTurnCount <= h.ExtraAttackEarly;
            }
            bool duel = SiegeDuelBonus != null && SiegeDuelBonus[seat] && Players.Count(p => p.HP > 0) == 2;
            return (window || duel) && !_earlyAttackUsed[seat];
        }

        /// <summary>攻城锤的额外普攻窗口是否还开着（AI 用来给首次普攻预留蓝，防止额外普攻被其他技能花光蓝而浪费）。</summary>
        public bool SiegeExtraLive(int seat) { return CanEarlyAttack(seat); }

        /// <summary>强攻：在攻击结算后累计连击，满3次记下追加攻击（待本次结算干净后发放）。
        /// ⑧冷却削弱（2026-10-15 用户裁定）：触发追加的回合与下一回合，命中维持连击链（更新锚点，不被⑦衰减）
        /// 但不计入新连击——追加频率从每3个自己回合一次降为每4个一次。</summary>
        void TrackFocus(int seat, int target)
        {
            if (!_heroes[seat].FocusFire || !Players[seat].Alive) return;
            int now = Players[seat].OwnTurnCount;
            if (_focusCooldown[seat] >= 0 && now <= _focusCooldown[seat] + 1)
            {
                _focusLastTurn[seat] = now;   // 冷却期命中维持链条（漏回合衰减不触发），但不计数
                return;
            }
            if (_focusSeat[seat] == target) _focusCount[seat]++;
            else { _focusSeat[seat] = target; _focusCount[seat] = 1; }
            _focusLastTurn[seat] = now;   // ⑦ 回合连续性衰减的锚点
            if (_focusCount[seat] >= 3)
            {
                _focusCount[seat] = 0;
                _focusBonus[seat] = target;
                _focusCooldown[seat] = now;   // ⑧ 触发回合起进入冷却
            }
        }

        /// <summary>强攻：当前连击目标（无连击/刚触发清零时返回 -1）。AI 索敌用，2026-10-09。</summary>
        public int FocusStreakTarget(int seat) { return _focusCount[seat] > 0 ? _focusSeat[seat] : -1; }
        /// <summary>强攻：当前连击已命中数（1=下一发是第2发，2=下一发是第3发=触发发）。AI 索敌用，2026-10-09。</summary>
        public int FocusStreakCount(int seat) { return _focusCount[seat]; }

        /// <summary>强攻：发放待结算的追加攻击。尊重目标合法性/结盟保护；pending 未清则等提交后再发。</summary>
        void TryFireFocusBonus(int seat)
        {
            int target;
            if (!_focusBonus.TryGetValue(seat, out target)) return;
            if (Pending != null) return;   // 等人类提交抉择后再发（SubmitGuardChoice 会再次调用）
            _focusBonus.Remove(seat);
            if (!ValidTarget(seat, target) || IsProtected(seat, target))
            {
                Log(Players[seat].Name + " 的【强攻】连击达成，但追加攻击的目标已不合法，奖励落空。");
                return;
            }
            Log(Players[seat].Name + " 的【强攻】触发：连续3次攻击同一目标，追加1次攻击！");
            DoAttack(seat, target, ActionKind.Attack);
        }

        void DoAttack(int attackerSeat, int victimSeat, ActionKind kind)
        {
            RaiseHostile(attackerSeat, victimSeat, kind);
            string tag = kind == ActionKind.PoisonAttack ? "（毒药）" : kind == ActionKind.ArsenalAttack ? "（军火库）" : "";
            Log(Players[attackerSeat].Name + " 攻击 " + Players[victimSeat].Name + tag + "！");
            ResolveAttack(attackerSeat, victimSeat, kind);
            TrackFocus(attackerSeat, victimSeat);
        }

        // ------------------------------------------------------------------
        // 各种结算
        // ------------------------------------------------------------------

        /// <summary>英雄升级（2026-10-21 用户裁定）：seat 消灭 n 张牌攒经验，
        /// 消灭 6 张升 2 级、再攒到 8 张升 3 级（每局最多升 2 次）。
        /// 计入渠道（草样口径）：攻击抽走/挡刀消耗/处决（驱魔·猎巫/审判官/冷酷追猎）/炸弹毁牌；
        /// 不计入：合成消耗/弃牌/淘汰遗物/青苹果被抢（没进弃牌堆）。EXP 封顶 8 不再累积。</summary>
        void AddExp(int seat, int n)
        {
            var p = Players[seat];
            if (!p.Alive || p.HeroLevel >= MAX_HERO_LEVEL) return;   // 升满3级后不再累积
            p.EXP = Math.Min(p.EXP + n, XP_LEVEL_3);
            while (p.HeroLevel < MAX_HERO_LEVEL)   // 升满即停，防 EXP 封顶后死循环
            {
                int need = p.HeroLevel == 0 ? XP_LEVEL_2 : XP_LEVEL_3;   // 下一次升级所需累计消灭数
                if (p.EXP < need) break;
                p.HeroLevel++;
                p.LevelTurn = p.OwnTurnCount;   // 攻城锤额外普攻窗口锚定"最近一次升级"
                GrantLevelUpReward(p, p.HeroLevel);
                Log("【升级】" + p.Name + " 已累计消灭 " + need + " 张牌——英雄升到 " + (p.HeroLevel + 1) + " 级！" + LevelUpRewardText(p.HeroLevel));
            }
        }

        /// <summary>升级奖励（2026-10-21 用户裁定）：2代升2级与升3级相同=方案C护盾。
        /// 3代引擎覆写为：升2级=1技能点；升3级=1技能点+回1点英雄HP。</summary>
        protected virtual void GrantLevelUpReward(PlayerState p, int level)
        {
            p.ShieldCharges = CRAFT_SHIELD_CHARGES;
            p.ShieldExpireOwnTurn = p.OwnTurnCount + LEVEL_SHIELD_TURNS;
        }

        /// <summary>升级奖励的播报文案（与 GrantLevelUpReward 同步覆写）。level=本次升级后的 HeroLevel（1=升2级，2=升3级）。</summary>
        protected virtual string LevelUpRewardText(int level)
        {
            return "获得【护盾】：持续 " + LEVEL_SHIELD_TURNS + " 回合，可吸收 " + CRAFT_SHIELD_CHARGES
                + " 点伤害（保护本体与整个手牌）。";
        }

        /// <summary>英雄回血（封顶英雄HP上限 HeroHP）。</summary>
        protected void HealHero(PlayerState p, int n)
        {
            if (!p.Alive || n <= 0) return;
            p.HP = Math.Min(p.HP + n, HeroHP);
        }

        // ------------------------------------------------------------------
        // 3代引擎（GameEngine3）用的受保护入口：语义与原名相同，仅开放可见性。
        // ------------------------------------------------------------------

        /// <summary>"视同攻击"的消灭结算（3代点燃等用）：走完整 ResolveDrawn——经验/亡语/青苹果/淘汰全部照常。</summary>
        protected void KillCardAsAttack(int attackerSeat, int ownerSeat, Card card) { ResolveDrawn(attackerSeat, ownerSeat, card, ActionKind.Attack); }

        /// <summary>牌进弃牌堆（盖阵亡戳）。3代释放法术消耗/点燃同步炸毁用。</summary>
        protected void DiscardNow(Card c) { ToDiscard(c); }

        /// <summary>给 seat 记 1 次消灭（升级经验）。3代点燃击杀等额外渠道用。</summary>
        protected void AddKillExp(int seat) { AddExp(seat, 1); }

        /// <summary>内部随机源（3代洗库等用）。</summary>
        protected int Rnd(int n) { return _rng.Next(n); }

        void ResolveAttack(int attackerSeat, int victimSeat, ActionKind kind)
        {
            // 美人计魅惑（2026-10-19 最终版=被动，用户裁定"代替挡刀的真实实现=让玩家本体替你承受普攻的伤害"——
            // "本体"非HP，是把攻击目标改到魅惑目标身上、走完整伤害判定流程：抽他的牌、可能掉他的HP、
            // 他的护盾/亡灵免疫/挡刀等一切正常结算）。对全部攻击实例生效（普攻/军火库/毒药/炸弹冲击波/强攻追加等，
            // 统一入口在此）。魅惑目标死亡或空手则攻击落回持有者本人。
            if (_charmGuardHolder == victimSeat && _charmGuardFrom >= 0 && _charmGuardFrom != attackerSeat)
            {
                var charmed = Players[_charmGuardFrom];
                if (charmed.Alive && charmed.Hand.Count > 0)
                {
                    Log("【美人计】魅惑生效：" + Players[attackerSeat].Name + " 对 " + Players[victimSeat].Name + " 的攻击被魅惑——"
                        + charmed.Name + " 代替承受！");
                    victimSeat = charmed.Seat;
                }
            }
            var v = Players[victimSeat];
            if (!v.Alive || v.Hand.Count == 0) return;
            // 致命一击（2026-10-19 重做=被动，用户："走攻击判定的时候加个if"；当晚加强：手牌上限4→5）：
            // 攻击HP=1且手牌（不含英雄牌——英雄牌是血条不是手牌，玩家视角的"手牌数"）≤5的玩家时必定命中其英雄——
            // 跳过抽牌/挡刀判定直接掉HP；护盾吸收仍先判定（护盾保护本体的语义不变）。
            // 被沉默（含被动全封）时不触发。对全部攻击实例生效（与魅惑重定向同一入口，先重定向后判定）。
            if (_heroes[attackerSeat].HasDeathblow && !_silenced.Contains(attackerSeat) && v.HP == 1
                && v.Hand.Count(c => c.Type != CardType.Hero) <= 5)
            {
                _lastHitBy[victimSeat] = attackerSeat;   // 击杀归属（凯旋判定）
                if (TryAbsorbShield(victimSeat, "这次攻击")) return;
                v.HP--;
                _apSinceHurt[v.Seat] = 0;   // 医师：受伤重置行动点计数
                NoteAppleCountdown(v);
                Log("【致命一击】" + Players[attackerSeat].Name + " 的杀招必定命中——" + v.Name + "（1点生命、手牌≤5张）的英雄被直接命中！"
                    + "损失1点HP（" + Math.Max(v.HP, 0) + "/" + v.MaxHP + "）。");
                CheckElimination(v);
                return;
            }
            var pool = v.Hand.Where(c => c.GuardProtectTurn < 0).ToList();   // 守卫护住的牌不参与抽牌判定
            if (pool.Count == 0) return;   // 手牌全部被护（英雄牌无法被护，实际不会发生）
            var drawn = pool[_rng.Next(pool.Count)];
            ResolveAttackAfterGuardRoll(attackerSeat, victimSeat, drawn, kind);
        }

        /// <summary>攻击判定的后半段：挡刀判定 → 结算被抽中的牌。</summary>
        void ResolveAttackAfterGuardRoll(int attackerSeat, int victimSeat, Card drawn, ActionKind kind)
        {
            var v = Players[victimSeat];
            _lastHitBy[victimSeat] = attackerSeat;   // 击杀归属（凯旋判定）：普攻/军火库/毒药/强攻追加/炸弹的公共入口
            // 方案C护盾（2026-10-13）：攻击落到本体之前先判定吸收——整个攻击被吞掉（不抽牌、不掉HP、不消耗挡刀身体）
            if (TryAbsorbShield(victimSeat, "这次攻击")) return;
            // （旧"魅惑=主目标出侍卫挡刀"实现已于 2026-10-19 废弃：改为攻击重定向到魅惑目标走完整流程，见 ResolveAttack。）
            // 被沉默者：挡刀身体全部失效；被封印的牌同样不能挡。
            // 挡刀身体：侍卫；合成牌女武神/圣骑士（人人可用）；牺牲英雄的牧师（PriestSacStyle 决定先后）。
            Card guard = null;
            List<Card> interceptors = null;
            if (!_silenced.Contains(victimSeat))
            {
                var shields = v.Hand.Where(c => !_silencedCards.Contains(c) && c.GuardProtectTurn < 0
                    && (c.Type == CardType.Guard || c.Type == CardType.Valkyrie || c.Type == CardType.Paladin
                        || (c.Type == CardType.Priest && _heroes[victimSeat].PriestSacrifice))).ToList();
                var priests = shields.Where(c => c.Type == CardType.Priest).ToList();
                if (_heroes[victimSeat].PriestSacrifice && PriestSacStyle == 1 && priests.Count > 0)
                    guard = priests[0];
                else
                    guard = shields.FirstOrDefault(c => c.Type == CardType.Guard)
                         ?? shields.FirstOrDefault(c => c.Type == CardType.Paladin)
                         ?? shields.FirstOrDefault(c => c.Type == CardType.Valkyrie)
                         ?? priests.FirstOrDefault();
                interceptors = shields;
            }
            if (guard != null && drawn.Type != CardType.Guard)
            {
                if (v.IsHuman)
                {
                    Pending = new PendingChoice { Kind = ChoiceKind.GuardIntercept, Seat = victimSeat, DrawnCard = drawn, AttackerSeat = attackerSeat, AttackKind = kind, Interceptors = interceptors };
                    return;
                }
                if (Brains[victimSeat].ChooseGuardIntercept(this, victimSeat, drawn, attackerSeat))
                {
                    if (OnGuardChoice != null) OnGuardChoice(victimSeat, guard.Type, drawn.Type, true, v.HP);
                    DiscardGuard(v, guard, attackerSeat, kind);
                    return;
                }
                if (OnGuardChoice != null) OnGuardChoice(victimSeat, guard.Type, drawn.Type, false, v.HP);
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
                _apSinceHurt[v.Seat] = 0;   // 医师：受伤重置行动点计数
                NoteAppleCountdown(v);
                Log(v.Name + " 的英雄牌被抽中！损失1点HP（" + Math.Max(v.HP, 0) + "/" + v.MaxHP + "），英雄牌回到手中。");
                CheckElimination(v);
                return;
            }
            bool cardSealed = _silencedCards.Contains(drawn);
            if (drawn.Type == CardType.Undead && !victimSilenced && !cardSealed)
            {
                Log(v.Name + " 的【亡灵】免疫攻击，回到手中。");
                if (OnIntel != null) OnIntel(victimSeat, victimSeat, CardType.Undead);   // 亡灵挡刀公开：其手牌中有亡灵
                return;
            }
            if (drawn.Type == CardType.Maid && kind == ActionKind.ArsenalAttack && !victimSilenced && !cardSealed)
            {
                Log(v.Name + " 的【侍女】免疫了军火库的攻击，回到手中。");
                return;
            }
            // 青苹果被攻击抽中：不进弃牌堆，而是被攻击者抢走（倒计时随之取消）
            if (drawn.Type == CardType.GreenApple)
            {
                v.Hand.Remove(drawn);
                drawn.AppleTimer = -1;
                Players[attackerSeat].Hand.Add(drawn);
                Log(Players[attackerSeat].Name + " 抽中了 " + v.Name + " 的【青苹果】——没有进入弃牌堆，而是被抢走了！");
                return;
            }
            // 3代·法术售后（2026-10-21 用户裁定）：抽中敌人手里"自己已经习得"的法术牌——
            // 与青苹果同口径据为己有（研习的是释放权限，抢来的是牌本体：对手学过的法术你也有机会拿到手）。
            // 没进弃牌堆 → 不计消灭经验。
            var g3steal = this as GameEngine3;
            if (g3steal != null && GameEngine3.IsSpellType(drawn.Type) && g3steal.HasSpell(attackerSeat, drawn.Type))
            {
                v.Hand.Remove(drawn);
                Players[attackerSeat].Hand.Add(drawn);
                Log(Players[attackerSeat].Name + " 抽中了 " + v.Name + " 的【" + drawn.Type.Name()
                    + "】——与自己已习得的法术共鸣，据为己有！");
                return;
            }
            // 合成牌身体（女武神/圣骑士，2点生命）：被攻击直接抽中也消耗1点生命（2026-10-13 修复文字与效果不符）；
            // 未耗尽则撑着回到手牌，耗尽才进弃牌堆（圣骑士触发亡语）。被沉默/封印则与普通牌一样直接阵亡。
            if (drawn.SynthHP >= 0 && !victimSilenced && !cardSealed)
            {
                drawn.SynthHP--;
                if (drawn.SynthHP > 0)
                {
                    Log(v.Name + " 的【" + drawn.Type.Name() + "】被攻击抽中，消耗1点生命（剩 " + drawn.SynthHP + " 点），撑着回到了手牌！");
                    return;
                }
                v.Hand.Remove(drawn);
                ToDiscard(drawn);
                AddExp(attackerSeat, 1);   // 英雄升级：攻击抽走
                Log(Players[attackerSeat].Name + " 抽走了 " + v.Name + " 的【" + drawn.Type.Name() + "】——生命耗尽，进入弃牌堆！");
                if (drawn.Type == CardType.Paladin)
                {
                    // 圣骑士亡语：套娃防护同挡刀路径，亡语不含圣骑士
                    var options = DiscardOptions(v.Seat, includePriest: false)
                        .Where(c => c.Id != drawn.Id && c.Type != CardType.Paladin)
                        .ToList();
                    if (options.Count == 0) Log("圣骑士亡语触发，但弃牌堆没有可回收的角色牌。");
                    else
                    {
                        Log(v.Name + " 的【圣骑士】亡语触发。");
                        OfferDiscardPick(v, options, "圣骑士亡语");
                    }
                }
                CheckElimination(v);
                return;
            }
            v.Hand.Remove(drawn);
            ToDiscard(drawn);
            AddExp(attackerSeat, 1);   // 英雄升级：攻击抽走
            Log(Players[attackerSeat].Name + " 抽走了 " + v.Name + " 的【" + drawn.Type.Name() + "】，进入弃牌堆。");
            if (victimSilenced || cardSealed)
            {
                // 被沉默（整个人或单张牌）：亡语不触发
            }
            else if (drawn.Type == CardType.Priest || drawn.Type == CardType.Paladin)
            {
                // 牧师/圣骑士亡语：不能回收英雄牌，也不能回收牧师（杜绝套娃）
                var options = DiscardOptions(v.Seat, includePriest: false).Where(c => c.Id != drawn.Id).ToList();
                if (options.Count == 0) Log("牧师亡语触发，但弃牌堆没有可回收的角色牌。");
                else
                {
                    Log(v.Name + " 的牧师亡语触发。");
                    OfferDiscardPick(v, options, "牧师亡语");
                }
            }
            else if ((drawn.Type == CardType.Guard || drawn.Type == CardType.Minister
                   || drawn.Type == CardType.ArmedGuard || drawn.Type == CardType.Warden) && HasArmGuard(v.Seat))
            {
                DeathrattleAttack(v.Seat);
            }
            CheckElimination(v);
        }

        /// <summary>恐怖分子炸弹结算（2026-10-13 用户改版）：到安放者的回合检查恐怖分子还活不活。
        /// 活着（在任何玩家手里/被藏/作人质都算）→ 爆炸：恐怖分子自己进弃牌堆（被大臣贿赂送人也不改目标），
        /// 然后对每个敌手（以安放者的敌我划线）各发动1次"无来源随机攻击"——等同普攻：随机抽1张手牌，
        /// 挡刀/2生命身体/亡灵免疫/亡语全部照常结算，但不记仇（无来源，AI不会因此寻仇安放者）；
        /// 已进弃牌堆（被抽走/处决等）→ 哑弹，效果自然消失（不退蓝不返还）。</summary>
        void ResolveBomb(Card bomb)
        {
            bomb.BombActive = false;
            int planter = bomb.BombPlanter;
            if (Discard.Contains(bomb))
            {
                Log(Players[planter].Name + " 的炸弹没能引爆：【恐怖分子】已先于爆炸阵亡，成了一枚哑弹。");
                return;
            }
            Log(Players[planter].Name + " 的炸弹引爆了！");
            // 从它现在所在的位置取出（可能在别人手里——被贿赂/扣押不改变爆炸目标）
            foreach (var pl in Players) pl.Hand.Remove(bomb);
            var hostage = _hostages.FirstOrDefault(x => x.Card == bomb);
            if (hostage != null) _hostages.Remove(hostage);
            ToDiscard(bomb);
            Log("【恐怖分子】与敌同归于尽，进入了弃牌堆。");
            // 冲击波入队（可能接在前一发暂停的队列后面），逐个结算
            foreach (var o in Players.Where(x => x.Alive && x.Seat != planter))
                _blastQueue.Add(new BlastHit { Planter = planter, Victim = o.Seat });
            ContinueBlast();
        }

        /// <summary>炸弹冲击波逐个结算。人类座位弹出抉择时暂停（Pending != null 即返回），
        /// 由三个 Submit* 提交点在清掉 Pending 后调用本方法从断点续炸。</summary>
        void ContinueBlast()
        {
            while (_blastQueue.Count > 0 && Pending == null && !GameOver)
            {
                var hit = _blastQueue[0];
                _blastQueue.RemoveAt(0);
                var v = Players[hit.Victim];
                if (!v.Alive || v.Hand.Count == 0) continue;
                var pool = v.Hand.Where(c => c.GuardProtectTurn < 0).ToList();   // 守卫护住的牌不参与抽牌判定
                if (pool.Count == 0) continue;
                var drawn = pool[_rng.Next(pool.Count)];
                Log("炸弹的冲击波袭向 " + v.Name + "！");
                if (drawn.Type == CardType.GreenApple)
                {
                    // 无来源攻击：青苹果不被"抢走"，而是被当场炸毁（倒数取消，进弃牌堆）
                    v.Hand.Remove(drawn);
                    drawn.AppleTimer = -1;
                    ToDiscard(drawn);
                    AddExp(hit.Planter, 1);   // 英雄升级：炸弹毁牌
                    Log(v.Name + " 的【青苹果】被炸成了碎片，进入弃牌堆（倒数取消）。");
                    continue;
                }
                // 等同普攻：挡刀判定（人类弹Pending暂停 / AI同步决策），拒挡才走进 ResolveDrawn
                ResolveAttackAfterGuardRoll(hit.Planter, v.Seat, drawn, ActionKind.Attack);
            }
            if (_blastQueue.Count == 0 || GameOver) _blastQueue.Clear();
        }

        /// <summary>炸弹被拆除：倒数中断，本回合已用标记清掉（"返还cd"——可以再次安放；蓝不返还）。
        /// 触发渠道：警察封印了安放中的恐怖分子 / police station 沉默了安放者。</summary>
        void DefuseBomb(Card bomb, string how)
        {
            if (!bomb.BombActive) return;
            bomb.BombActive = false;
            bomb.BombPlanter = -1;
            bomb.BombDueTurn = -1;
            bomb.UsedSkillTurn = -1;   // 返还cd：本回合可再次安放
            Log("【恐怖分子】的炸弹被拆除了（" + how + "）：倒数中断，安放技能冷却返还（蓝不返还）。");
        }

        /// <summary>seat 名下是否有倒数中的炸弹（UI 状态栏显示用）。</summary>
        public bool HasTickingBomb(int seat)
        {
            return AllCards().Any(c => c.BombActive && c.BombPlanter == seat);
        }

        /// <summary>挡刀结算。合成牌挡刀身体（女武神/圣骑士，2点生命）：挡1刀扣1点生命，还活着就回到手牌；
        /// 生命耗尽才进入弃牌堆（圣骑士耗尽时触发亡语回收）。侍卫/牺牲牧师的挡刀照旧被消耗；
        /// 侍卫挡刀触发带刀侍卫亡语反击（2026-10-08 用户裁定：合成牌挡刀必须体现2点生命）。</summary>
        void DiscardGuard(PlayerState v, Card shield, int attackerSeat, ActionKind kind)
        {
            if (shield.SynthHP >= 0)
            {
                // 合成牌挡刀：-1生命；>0 回手，=0 进弃牌堆
                shield.SynthHP--;
                if (shield.SynthHP > 0)
                {
                    Log(v.Name + " 的【" + shield.Type.Name() + "】挡下了攻击（剩余 " + shield.SynthHP + " 点生命），回到手牌！");
                    return;   // 不消耗、回到手牌
                }
                v.Hand.Remove(shield);
                ToDiscard(shield);
                AddExp(attackerSeat, 1);   // 英雄升级：挡刀消耗
                Log(v.Name + " 的【" + shield.Type.Name() + "】生命耗尽，代替进入了弃牌堆！");
                if (shield.Type == CardType.Paladin)
                {
                    // 圣骑士亡语：进入弃牌堆就触发——生命耗尽被消耗也算。套娃防护：亡语不含圣骑士。
                    var options = DiscardOptions(v.Seat, includePriest: false)
                        .Where(c => c.Id != shield.Id && c.Type != CardType.Paladin)
                        .ToList();
                    if (options.Count == 0) Log("圣骑士亡语触发，但弃牌堆没有可回收的角色牌。");
                    else
                    {
                        Log(v.Name + " 的【圣骑士】亡语触发。");
                        OfferDiscardPick(v, options, "圣骑士亡语");
                    }
                }
                return;
            }
            v.Hand.Remove(shield);
            ToDiscard(shield);
            AddExp(attackerSeat, 1);   // 英雄升级：挡刀消耗
            Log(v.Name + " 的【" + shield.Type.Name() + "】挺身而出，代替进入弃牌堆！");
            if (shield.Type == CardType.Guard && HasArmGuard(v.Seat) && !_silenced.Contains(v.Seat)) DeathrattleAttack(v.Seat);
            if (shield.Type == CardType.Priest)
            {
                // 牺牲英雄牧师的亡语：进入弃牌堆就触发——挡刀被消耗也算（2026-10-07 用户裁定）。牧师亡语不含牧师。
                var options = DiscardOptions(v.Seat, includePriest: false)
                    .Where(c => c.Id != shield.Id)
                    .ToList();
                if (options.Count == 0) Log("牧师亡语触发，但弃牌堆没有可回收的角色牌。");
                else
                {
                    Log(v.Name + " 的【牧师】亡语触发。");
                    OfferDiscardPick(v, options, "牧师亡语");
                }
            }
        }

        IEnumerable<Card> AllCards()
        {
            foreach (var p in Players) foreach (var c in p.Hand) yield return c;
            foreach (var c in Discard) yield return c;
            foreach (var c in SideDeck) yield return c;
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
            var pool = t.Hand.Where(c => c.GuardProtectTurn < 0).ToList();   // 守卫护住的牌不参与抽牌判定
            if (pool.Count == 0) return;
            var c = pool[_rng.Next(pool.Count)];
            if (OnIntel != null) OnIntel(exorcistSeat, targetSeat, c.Type);   // 抽到的牌型公开播报（含亡灵）
            if (c.Type == CardType.Undead
                || (_heroes[exorcistSeat].WitchHunt && c.Type == CardType.Witch))   // 猎巫行动：驱魔抽中女巫→处决（2026-10-08 加回英雄池）
            {
                t.Hand.Remove(c);
                ToDiscard(c);
                AddExp(exorcistSeat, 1);   // 英雄升级：处决
                Log(Players[exorcistSeat].Name + " 的驱魔师当场处决了 " + t.Name + " 的【" + c.Type.Name() + "】！");
            }
            else
            {
                Log("驱魔抽中【" + c.Type.Name() + "】，不是亡灵，放回去了。（驱魔方已获知该情报）");
            }
        }

        void ResolveSeize(int seizerSeat, int targetSeat)
        {
            var t = Players[targetSeat];
            // 随机选一张非英雄牌封印；命中的牌型公开播报（与驱魔师一致）。守卫护住的牌不参与选取。
            var c = t.Hand.Where(x => x.Type != CardType.Hero && x.GuardProtectTurn < 0).OrderBy(_ => _rng.Next()).FirstOrDefault();
            if (c == null)
            {
                Log(Players[seizerSeat].Name + " 的警察找不到可沉默的牌（对方只剩英雄牌），行动落空。");
                return;
            }
            _silencedCards.Add(c);
            c.SealedBy = seizerSeat;   // 2026-10-19：记封印者（美人计魅惑只解主目标的封印）
            if (!_silencedCardsByOwner.TryGetValue(targetSeat, out var l)) { l = new List<Card>(); _silencedCardsByOwner[targetSeat] = l; }
            l.Add(c);
            if (OnIntel != null) OnIntel(seizerSeat, targetSeat, c.Type);   // 扣押命中的牌型公开播报
            Log(Players[seizerSeat].Name + " 的警察封住了 " + t.Name + " 的【" + c.Type.Name() + "】："
                + t.Name + " 的下个回合，该牌无法使用技能、被动也失效。（命中牌型公开）");
            if (c.BombActive) DefuseBomb(c, "被警察封印");   // 2026-10-13：安放中的恐怖分子被封印 = 拆弹+返还cd
        }

        /// <summary>典狱长·扣押：随机拿对方1张非英雄牌到自己手中作人质并封印；
        /// 原拥有者的第2个回合结束归还并解除封印（EndTurn处理，ReleaseTurn = 扣押时原拥有者 OwnTurnCount + 2）。</summary>
        void ResolveHostage(int takerSeat, int targetSeat)
        {
            var t = Players[targetSeat];
            var c = t.Hand.Where(x => x.Type != CardType.Hero && x.GuardProtectTurn < 0).OrderBy(_ => _rng.Next()).FirstOrDefault();
            if (c == null)
            {
                Log(Players[takerSeat].Name + " 的典狱长找不到可扣押的牌（对方只剩英雄牌），行动落空。");
                return;
            }
            t.Hand.Remove(c);
            Players[takerSeat].Hand.Add(c);
            _silencedCards.Add(c);
            c.SealedBy = takerSeat;   // 2026-10-19：记扣押者（美人计魅惑只归还主目标的人质）
            if (!_silencedCardsByOwner.TryGetValue(targetSeat, out var l)) { l = new List<Card>(); _silencedCardsByOwner[targetSeat] = l; }
            l.Add(c);
            _hostages.Add(new Hostage { Card = c, OwnerSeat = targetSeat, ReleaseTurn = t.OwnTurnCount + 2 });
            if (OnIntel != null) OnIntel(takerSeat, targetSeat, c.Type);   // 扣押命中的牌型公开播报（与警察一致）
            Log(Players[takerSeat].Name + " 的【典狱长】扣押了 " + t.Name + " 的【" + c.Type.Name() + "】作为人质（封印）："
                + t.Name + " 的第2个回合结束归还并解除封印（人质扣2回合）。");
            CheckElimination(t);
        }

        /// <summary>审判官·审判：随机揭示对手1张牌；亡灵/女巫（异端）当场处决，否则放回。揭示的牌型公开播报。</summary>
        void ResolveJudge(int judgeSeat, int targetSeat)
        {
            var t = Players[targetSeat];
            var pool = t.Hand.Where(c => c.GuardProtectTurn < 0).ToList();   // 守卫护住的牌不参与揭示判定
            if (pool.Count == 0) return;
            var c = pool[_rng.Next(pool.Count)];
            if (OnIntel != null) OnIntel(judgeSeat, targetSeat, c.Type);   // 揭示的牌型公开播报
            if (c.Type == CardType.Undead || c.Type == CardType.Witch)
            {
                t.Hand.Remove(c);
                ToDiscard(c);
                AddExp(judgeSeat, 1);   // 英雄升级：处决
                Log(Players[judgeSeat].Name + " 的【审判官】揭示了 " + t.Name + " 的【" + c.Type.Name() + "】——异端！当场处决，进入弃牌堆。");
            }
            else
            {
                Log("审判揭示【" + c.Type.Name() + "】，不是异端，放了回去。（审判官方已获知该情报）");
            }
        }

        void ResolveBribe(int briberSeat, int targetSeat, int giveCardId)
        {
            var me = Players[briberSeat];
            if (targetSeat < 0 || targetSeat >= Players.Count || targetSeat == briberSeat || !Players[targetSeat].Alive) return;
            var give = me.Hand.FirstOrDefault(c => c.Id == giveCardId && c.Type != CardType.Hero && c.GuardProtectTurn < 0);
            if (give == null) return;
            me.Hand.Remove(give);
            Players[targetSeat].Hand.Add(give);
            // 美人计（2026-10-19 最终版=被动）：交出侍女 = 对方被魅惑——到贿赂者的下个回合开始，
            // 贿赂者受到的攻击转移到魅惑目标身上走完整伤害判定（ResolveAttack 统一入口）。
            bool charm = _heroes[briberSeat].MaidCharm && give.Type == CardType.Maid;
            if (charm)
            {
                _charmGuardHolder = briberSeat;
                _charmGuardFrom = targetSeat;
            }
            _alliances.Add(new[] { targetSeat, briberSeat, 1 });
            // 2026-10-14⑥ 贿赂断强攻：被买停战的强攻若正连击贿赂者，连击清零——否则停战一过期第3发连击照发，贿赂就没"断"成。
            if (_focusSeat[targetSeat] == briberSeat)
            {
                _focusSeat[targetSeat] = -1;
                _focusCount[targetSeat] = 0;
                Log(Players[targetSeat].Name + " 对 " + me.Name + " 的【强攻】连击被贿赂打断，从头再来！");
            }
            Log(me.Name + " 把【" + give.Type.Name() + "】交给 " + Players[targetSeat].Name + " 作为贿赂：结盟生效，到 "
                + me.Name + " 的下回合开始前，" + Players[targetSeat].Name + " 不能攻击 " + me.Name + "。"
                + (charm ? "【美人计】侍女牵线，" + Players[targetSeat].Name + " 被魅惑：期间 " + me.Name + " 受到的攻击由其代替承受！" : ""));
            CheckElimination(me);
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
                    _lastHitBy[o.Seat] = extorterSeat;   // 击杀归属（凯旋判定）
                    if (!TryAbsorbShield(o.Seat, "勒索的1点英雄伤害"))
                    {
                        o.HP--;
                        _apSinceHurt[o.Seat] = 0;   // 医师：受伤重置行动点计数
                        NoteAppleCountdown(o);
                        Log(o.Name + " 的英雄牌被勒索打中：损失1点HP（" + Math.Max(o.HP, 0) + "/" + o.MaxHP + "）！");
                        CheckElimination(o);
                    }
                    RaiseHostile(extorterSeat, o.Seat, ActionKind.Extort);
                }
                else
                {
                    var taken = o.Hand.Where(c => c.Type == type && c.GuardProtectTurn < 0).ToList();   // 守卫护住的牌不参与勒索
                    foreach (var c in taken) { o.Hand.Remove(c); me.Hand.Add(c); }
                    if (taken.Count > 0)
                    {
                        RaiseHostile(extorterSeat, o.Seat, ActionKind.Extort);
                        Log("勒索了 " + o.Name + " 的 " + taken.Count + " 张【" + type.Name() + "】。");
                        CheckElimination(o);
                    }
                    else Log(o.Name + " 没有【" + type.Name() + "】。");
                }
            }
        }

        /// <summary>牌进入弃牌堆：盖阵亡回合戳（女巫解药"上一回合阵亡"判定用）+ 死亡出厂重置
        /// （2026-10-22：限定技/定时器/炸弹/封印等状态清零，牌被回收后下一条命从全白状态开始）。</summary>
        void ToDiscard(Card c)
        {
            c.ResetForNewLife();
            c.DiedStamp = _turnStamp;
            Discard.Add(c);
        }

        /// <summary>当前回合戳（测试构造弃牌堆时用）。</summary>
        public int CurrentStamp { get { return _turnStamp; } }

        /// <summary>弃牌堆可回收的角色牌（每种类型留一张）。牧师亡语不含牧师。
        /// freshSince ≥ 0 时只保留该戳之后阵亡的牌（女巫解药：上一回合内阵亡才救得活）。
        /// ReviveSynthStyle[seat]==1 时排除合成牌（2026-10-08 平衡方式1：禁止复活合成牌）。</summary>
        List<Card> DiscardOptions(int seat, bool includePriest, int freshSince = -1)
        {
            bool banSynth = ReviveSynthStyle[seat] == 1;
            return Discard.Where(c => c.Type.IsRole() && c.Type != CardType.Hero && (includePriest || c.Type != CardType.Priest)
                                     && (freshSince < 0 || c.DiedStamp >= freshSince)
                                     && (!banSynth || c.SynthHP < 0))
                          .GroupBy(c => c.Type).Select(g => g.First()).ToList();
        }

        /// <summary>回收落地（AI自动路径、人类提交与凯旋战利品共用）：入手的合成牌若该座位开了平衡方式2，生命重置为1点。
        /// fromSeat ≥ 0 时 = 凯旋战利品：牌来自被淘汰者的手牌而非弃牌堆，不触发合成牌生命重置。</summary>
        void CompleteDiscardPick(PlayerState owner, Card card, int fromSeat = -1)
        {
            if (fromSeat >= 0)
            {
                Players[fromSeat].Hand.Remove(card);
                owner.Hand.Add(card);
                Log(owner.Name + " 从 " + Players[fromSeat].Name + " 的遗物中挑选了【" + card.Type.Name() + "】。");
            }
            else
            {
                Discard.Remove(card);
                if (ReviveSynthStyle[owner.Seat] == 2 && card.SynthHP >= 0) card.SynthHP = 1;   // 平衡方式2：复活后只剩1点生命
                owner.Hand.Add(card);
                Log(owner.Name + " 从弃牌堆回收了【" + card.Type.Name() + "】。"
                    + (card.SynthHP == 1 && ReviveSynthStyle[owner.Seat] == 2 ? "（生命仅剩1点）" : ""));
            }
            if (OnIntel != null) OnIntel(owner.Seat, owner.Seat, card.Type);   // 回收/挑选的牌公开播报
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
                CompleteDiscardPick(owner, card);
            }
        }

        // ------------------------------------------------------------------
        // 人类玩家的Pending提交
        // ------------------------------------------------------------------

        /// <summary>挡刀抉择提交。interceptCardId 指定用哪张牌挡（侍卫/女武神/圣骑士；牺牲英雄还有牧师）；
        /// 不指定或指定的牌已不在手时，回退到与AI一致的优先序：牺牲英雄按 PriestSacStyle，其余 侍卫→圣骑士→女武神→牧师。</summary>
        public void SubmitGuardChoice(bool intercept, int interceptCardId = -1)
        {
            var pc = Pending;
            if (pc == null || pc.Kind != ChoiceKind.GuardIntercept) return;
            Pending = null;
            var v = Players[pc.Seat];
            if (intercept)
            {
                Card shield = interceptCardId >= 0
                    ? v.Hand.FirstOrDefault(c => c.Id == interceptCardId
                        && (c.Type == CardType.Guard || c.Type == CardType.Valkyrie || c.Type == CardType.Paladin
                            || (c.Type == CardType.Priest && _heroes[pc.Seat].PriestSacrifice)))
                    : null;
                if (shield == null)
                {
                    var shields = v.Hand.Where(c =>
                            c.Type == CardType.Guard || c.Type == CardType.Valkyrie || c.Type == CardType.Paladin
                            || (c.Type == CardType.Priest && _heroes[pc.Seat].PriestSacrifice)).ToList();
                    var priests = shields.Where(c => c.Type == CardType.Priest).ToList();
                    shield = (_heroes[pc.Seat].PriestSacrifice && PriestSacStyle == 1 && priests.Count > 0)
                        ? priests[0]
                        : shields.FirstOrDefault(c => c.Type == CardType.Guard)
                          ?? shields.FirstOrDefault(c => c.Type == CardType.Paladin)
                          ?? shields.FirstOrDefault(c => c.Type == CardType.Valkyrie)
                          ?? priests.FirstOrDefault();
                }
                if (shield != null) { DiscardGuard(v, shield, pc.AttackerSeat, pc.AttackKind); TryFireFocusBonus(pc.AttackerSeat); ResumeBlast(); return; }
            }
            Log(v.Name + " 选择保留挡刀牌，不挡刀。");
            ResolveDrawn(pc.AttackerSeat, pc.Seat, pc.DrawnCard, pc.AttackKind);
            EndTurnIfCurrentDead();
            TryFireFocusBonus(pc.AttackerSeat);
            ResumeBlast();
        }

        /// <summary>炸弹冲击波断点续炸：提交点清掉 Pending 后，若爆炸队列还没炸完则继续。</summary>
        void ResumeBlast()
        {
            if (_blastQueue.Count > 0 && Pending == null && !GameOver) ContinueBlast();
        }

        public void SubmitDiscardPick(int cardId)
        {
            var pc = Pending;
            if (pc == null || pc.Kind != ChoiceKind.PickFromDiscard) return;
            var card = pc.Options.FirstOrDefault(c => c.Id == cardId);
            if (card == null) return;
            Pending = null;
            // 凯旋（人类）：牌来自被淘汰者的遗物（此时其手牌尚未丢弃）
            CompleteDiscardPick(Players[pc.Seat], card, _pendingElim != null ? _pendingElim.Seat : -1);
            if (_pendingElim != null) { var pe = _pendingElim; _pendingElim = null; FinishElimination(pe); }
            EndTurnIfCurrentDead();
            ResumeBlast();
        }

        public void SubmitDeathrattleTarget(int targetSeat)
        {
            var pc = Pending;
            if (pc == null || pc.Kind != ChoiceKind.DeathrattleAttack) return;
            if (!Players[pc.Seat].Alive) { Pending = null; ResumeBlast(); return; }
            if (targetSeat < 0 || targetSeat >= Players.Count || targetSeat == pc.Seat
                || !Players[targetSeat].Alive || Players[targetSeat].Hand.Count == 0
                || IsProtected(pc.Seat, targetSeat)) return;
            Pending = null;
            RaiseHostile(pc.Seat, targetSeat, ActionKind.Attack);
            Log(Players[pc.Seat].Name + " 的亡语攻击打向 " + Players[targetSeat].Name + "！");
            ResolveAttack(pc.Seat, targetSeat, ActionKind.Attack);
            EndTurnIfCurrentDead();
            ResumeBlast();
        }

        // ------------------------------------------------------------------
        // 回合结束 / 淘汰
        // ------------------------------------------------------------------

        void EndTurn()
        {
            _freeSkills.Remove(Current);   // save只持续本回合
            _silenced.Remove(Current);     // police station的沉默到被沉默者的回合结束解除
            // 典狱长的人质：到原拥有者的"第2个回合结束"归还并解除封印（2026-10-14：人质扣2回合；解封随归还一并处理）。
            // 人质可能在归还前被攻击抽走/拿去合成——从手牌、弃牌堆里都要能找到并取出（2026-10-15：藏牌区已移除，牌不离手）。
            for (int i = _hostages.Count - 1; i >= 0; i--)
            {
                if (_hostages[i].OwnerSeat != Current) continue;
                if (Players[Current].OwnTurnCount < _hostages[i].ReleaseTurn) continue;   // 还没到第2个回合结束
                var h = _hostages[i];
                _hostages.RemoveAt(i);
                var hc = h.Card;
                var holder = Players.FirstOrDefault(x => x.Hand.Contains(hc));
                if (holder != null) holder.Hand.Remove(hc);
                else if (Discard.Contains(hc)) Discard.Remove(hc);
                _silencedCards.Remove(hc);   // 归还同时解封
                hc.SealedBy = -1;
                var owner = Players[h.OwnerSeat];
                if (owner.Alive)
                {
                    owner.Hand.Add(hc);
                    Log(owner.Name + " 的人质【" + hc.Type.Name() + "】被归还，解除封印。");
                }
                else ToDiscard(hc);
            }
            // 冷酷追猎（2026-10-19 重做，用户："当你本回合行动点即将耗尽且还没有普攻，则随机处决一名被封印的敌方单位"）：
            // "行动点即将耗尽"=剩余蓝不足一次普攻（MP<1，按普攻档1行动点裁定）；"敌方"=任一存活对手手中；
            // 任何人的封印都算（用户明示的收益点）。不耗蓝、不占普攻槽（白送的处决窗口）；
            // 被沉默（含被动全封）时不触发。在解封之前结算——本回合刚封印的牌也来得及吃。
            {
                var hunter = Players[Current];
                if (_heroes[Current].ColdHunt && !_silenced.Contains(Current) && hunter.Alive
                    && !BasicAttackUsed && hunter.MP < COST_BASIC_ATTACK)
                {
                    var prey = Players.Where(p => p.Seat != Current && p.Alive)
                        .SelectMany(p => p.Hand.Where(c => _silencedCards.Contains(c))).ToList();
                    if (prey.Count > 0)
                    {
                        var victimCard = prey[_rng.Next(prey.Count)];
                        var owner = Players.First(p => p.Hand.Contains(victimCard));
                        owner.Hand.Remove(victimCard);
                        victimCard.SealedBy = -1;
                        _silencedCards.Remove(victimCard);
                        ToDiscard(victimCard);
                        AddExp(Current, 1);   // 英雄升级：处决
                        Log("【冷酷追猎】" + hunter.Name + " 的行动点耗尽且本回合未普攻——处决了 " + owner.Name
                            + " 被封印的【" + victimCard.Type.Name() + "】！");
                    }
                }
            }
            // 警察的牌沉默：到拥有者的回合结束解除（人质除外——人质的封印跟着扣押期走，归还时一并解除）
            if (_silencedCardsByOwner.TryGetValue(Current, out var sc))
            {
                foreach (var c in sc) if (!_hostages.Any(h2 => h2.Card == c)) { _silencedCards.Remove(c); c.SealedBy = -1; }
                _silencedCardsByOwner.Remove(Current);
            }
            int n = Players.Count, old = Current, seat = Current;
            while (true)
            {
                seat = (seat + 1) % n;
                if (seat == Current) break;                       // 绕场一周回到自己（其余全部死亡）
                if (!Players[seat].Alive) continue;
                break;
            }
            Current = seat;
            if (seat < old) TurnNumber++;
            StartTurn(Current);
        }

        /// <summary>方案C护盾（2026-10-13 用户设计）：尝试吸收1次对玩家的伤害。有充能时吸收成功（扣1充能）并播报。
        /// 吸收范围：任何攻击实例（普攻/毒药/军火库/强攻追加/炸弹随机攻击——在抽牌前整个吞掉，同时保护本体HP与整个手牌），
        /// 以及勒索【英雄】的1点直伤。到期/吸满即消散（Charges=0）。</summary>
        bool TryAbsorbShield(int victimSeat, string sourceDesc)
        {
            var v = Players[victimSeat];
            if (v.ShieldCharges <= 0) return false;
            v.ShieldCharges--;
            Log(v.Name + " 的【护盾】吸收了" + sourceDesc + "（护盾耗尽）！");
            return true;
        }

        /// <summary>HP 归零即出局（2026-10-13 起工人护盾废止：工人回归牌库，效果是行动点+1，不再是生存资源）。</summary>
        void CheckElimination(PlayerState p)
        {
            if (!p.Alive || p.HP > 0) return;
            p.Alive = false;
            EliminationOrder.Add(p.Seat);   // 出局顺序：倒数第一是第二名，用于锦标赛计分
            Log(p.Name + " HP归零，出局！");
            // 凯旋（2026-10-14）：击杀归属者（最近一次对其造成伤害者）获得2回合护盾，并可从其遗物中挑1张非英雄牌
            int killer = _lastHitBy[p.Seat];
            if (killer >= 0 && killer != p.Seat && Players[killer].Alive && _heroes[killer].Triumph)
            {
                var kp = Players[killer];
                kp.ShieldCharges = 1;
                kp.ShieldExpireOwnTurn = kp.OwnTurnCount + TRIUMPH_SHIELD_TURNS;
                Log(kp.Name + " 的英雄【凯旋】奏凯：获得【护盾】（持续 " + TRIUMPH_SHIELD_TURNS + " 回合，吸收1点伤害）！");
                var spoils = p.Hand.Where(c => c.Type != CardType.Hero).ToList();   // 英雄牌随主人退场，不可挑选
                if (spoils.Count > 0)
                {
                    if (kp.IsHuman)
                    {
                        // 人类：弹抉择，手牌丢弃/游戏结束判定延后到提交（SubmitDiscardPick → FinishElimination）
                        Pending = new PendingChoice { Kind = ChoiceKind.PickFromDiscard, Seat = killer, Options = spoils, Reason = "凯旋·战利品" };
                        _pendingElim = p;
                        return;
                    }
                    int pickId = Brains[killer].ChooseFromDiscard(this, killer, spoils);
                    var pick = spoils.FirstOrDefault(c => c.Id == pickId) ?? spoils[0];
                    CompleteDiscardPick(kp, pick, p.Seat);
                }
            }
            FinishElimination(p);
        }

        /// <summary>淘汰收尾：丢弃全部手牌（含英雄牌）、清理封印、判定游戏结束。凯旋（人类）的战利品抉择提交后调用。</summary>
        void FinishElimination(PlayerState p)
        {
            foreach (var c in p.Hand.ToList()) { p.Hand.Remove(c); ToDiscard(c); }
            if (_silencedCardsByOwner.TryGetValue(p.Seat, out var sc))
            {
                foreach (var c in sc) { _silencedCards.Remove(c); c.SealedBy = -1; }
                _silencedCardsByOwner.Remove(p.Seat);
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
