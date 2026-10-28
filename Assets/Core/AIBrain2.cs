using System;
using System.Collections.Generic;
using System.Linq;

namespace SandKill.Core
{
    /// <summary>真人占位（2代）：真人由UI驱动，引擎不会调用真人大脑的方法。</summary>
    public class HumanBrain2 : IPlayerBrain2
    {
        public bool IsHuman { get { return true; } }
        public GameAction ChooseAction(GameEngine2 g, int seat, List<GameAction> legal) { throw new InvalidOperationException("真人由UI驱动"); }
        public bool ChooseGuardIntercept(GameEngine2 g, int seat, Card drawn, int attackerSeat) { throw new InvalidOperationException("真人由UI驱动"); }
        public int ChooseFromDiscard(GameEngine2 g, int seat, List<Card> options) { throw new InvalidOperationException("真人由UI驱动"); }
        public int ChooseDeathrattleTarget(GameEngine2 g, int seat, List<int> candidates) { throw new InvalidOperationException("真人由UI驱动"); }
    }

    /// <summary>
    /// AI调参权重：默认值是设计值；自对弈/调参时构造不同实例对比。
    /// 所有权重集中在这一处，逻辑不随参数变复杂。
    /// </summary>
    public class AIWeights
    {
        // 辅助技目标选择（警察/亡语/贿赂对象用威胁评估）
        public float HandSizeThreat = 1.0f;    // 威胁分手牌项 = 手牌价值估算×此系数（2026-10-04 起张数制改为价值估算制：已知牌按KeepValue逐张计、未知按全库均值计，原"张数×2"废弃）
        public float LowHpBonus = 2.0f;        // 低血量加成（接近斩杀线）
        public float KillLineBonus = 0f;       // 击杀所需攻击次数少的优先。线性形式实测只有副作用（0.15→-2.4%），默认关闭留待更好的公式
        public float Grudge = 0.35f;           // 记仇
        public float ArsenalMemory = 4f;       // 【已废弃2026-10-04】军火库单独针对项并入价值估算制（已知军火库=9分计入手牌估算），字段保留兼容
        public float HpThreat = 7f;            // 威胁分：目标每1点当前HP +N（血量入威胁：满血强者=头号公敌；2026-10-04 用户设计，7/8/9 对照：7→1.005分/36.6%胜，8→0.998/35.9%，9→0.995/34.8%，7 双指标最优定稿）
        // 普攻独立索敌（轮流火力，放弃绝对理性）——攻击类行动（普攻/军火库/毒药）专用
        public float StalenessBonus = 2.0f;    // 空闲回合加分：我越久没打的目标分越高（轮流火力的核心）
        public float HitThisRoundBonus = 3.0f; // 本回合没人碰过的目标加分（火力散布，没人能靠装死隐身）
        public float JustHitPenalty = 4.0f;    // 自己上一回合刚打过的目标减分
        public float SameTurnPenalty = 1f;     // 本回合已打过（原100=禁双杀；1=几乎完全开放双杀，只留轻微倾向）
        public float DuelAttackBonus = 10f;    // 1v1：唯一目标攻击分拉满
        public float LastHitBonus = 0f;        // 目标只剩1HP的斩杀奖励（已废，保留字段兼容）
        public float HpAttackBonus = 1f;       // 合纵：自认不是最强时，目标每1点当前HP +1（弱者联合压强者/健康者）
        public float HpAttackBonusLeader = -1f;// 连横（各个击破）：自认最强时HP分取负，优先斩杀弱者
        public float ArsenalAttackBonus = 1f;  // 【已废弃2026-10-04】军火库单独攻击加分并入价值估算制，字段保留兼容
        public float SealedPriestAttackBonus = 1f; // 攻击分：对手有被警察封印的牧师牌时 +N（封印牧师不能挡刀/亡语回收，打他最划算；2026-10-04 用户设计）
        public float NextSeatBonus = 0f;       // 下家优先（2026-10-08 用户理论"痛击下家"实验）：攻击分加成，目标=下家座位(我+1)时生效。理论：打下家一手牌后，下家的下一家在手牌压力下大概率打你而不是打下家→火力从自己身上挪走；0=现行（关闭）
        public float NextSeatEarlyBonus = 0f;  // 开局痛击下家（2026-10-08 用户：纳什均衡下全时段改策略被轮转对冲，但开局手牌对称、轮转未建立，定向打击可能有超额收益）：同上但仅自己的前2个回合生效；与攻城锤开局双普攻窗口叠加待测；0=关闭
        public int SupportStyle = 0;           // 封印vs驱魔优先级（2026-10-04 测试任务1）：0=现行EV排序 1=驱魔总优先 2=封印总优先 3=轮转（上次封印这次驱魔）
        public int ReviveStyle = 1;            // 牧师复活/解药回收目标：1=最高价值（2026-10-08 用户裁定恢复为全局默认——医师体系依赖回收质量，全局5会把医师从40%拖到29%）。可选旋钮：5=优先侍卫（1.105分/36.2%唯一超噪声信号，留着做单英雄/未来英雄的策略选项，勿全局开）2=驱魔师 3=警察 4=亡灵 6=守卫 7=青苹果
        public float ThreatInAttack = 0.75f;   // 攻击分 = 轮转基础 + 威胁分×此系数（0=完全解耦；实验：0.25→-0.026，0.5→+0.035，0.75→+0.058，1.0→+0.018）
        public float FocusStreak = 1.5f;       // 【已废弃2026-10-09】强攻扁平连击加分被graduated方案取代（第2发+2/第3发+10），字段保留兼容
        public float FocusSecondBonus = 2f;    // 强攻graduated（2026-10-09 用户设计）：连击第2发（已中1）攻击分+2
        public float FocusThirdBonus = 10f;    // 强攻graduated（2026-10-09 用户设计）：连击第3发=触发发（已中2）攻击分+10，锁死触发；触发后计数清零→转火窗口（轮转接管，仅免费攻击留下的1连=+2软续）
        // 资源纪律
        public float PoisonKillHp = 2f;        // 斩杀判定：索敌选中的目标HP≤N时毒药出手（普攻+毒≈2点斩杀线）
        public float PoisonLateTurn = 6f;      // 第N轮后不再等（残局）；有侍卫保护时生存压力判定让位于保护判定
        public int AntidoteValueMin = 5;       // 解药平衡轴·松端：无生存压力时，弃牌堆最高价值≥此值才回收
        public int AntidoteValueMinPressured = 5;// 解药平衡轴·紧端：自己承压（残血/被集火）时标准放宽到此值。实测放宽到3无收益（±噪声），默认=5即轴关闭，保留供调参
        public int PriestAntidoteBoost = 2;    // 牺牲专属：上回合有牧师死亡时解药门槛降低量（只影响牺牲玩家）
        public int ArmGuardMinBodies = 2;      // 带刀侍卫布防需要的侍卫+大臣数
        public int BlockStyle = 1;             // 挡刀风格（2026-10-09 去同质化·挡刀缺口）：1=现行教学版（默认）2=见刀就挡 3=保血版
                                               // 教学版英雄牌分支显式化用户裁定：医师（HeroRoster Id=0）满血不挡英雄牌——挨1刀正好启动回血引擎（每耗6AP回1HP），
                                               // 与"残血才挡"在数值上等价，写成显式判断是为文档化裁定、防未来重构误伤。
                                               // 保血版：英雄牌永远挡（挡1HP就是赚），医师满血例外不挡；其余牌挡刀阈值 8→5 放宽。
                                               // 2026-10-09 用户裁定例外：风格2 不挡亡灵（被抽中自动免疫，挡它纯浪费）与牧师（被抽中触亡语回收，挡它剥夺自己回收）；
                                               // 全风格把带刀侍卫/军机大臣按≥8分档挡（KeepValue=7 不动全局表，挡刀价值另算，见 ChooseGuardIntercept）。
        public bool AfkUntilDuel = false;      // 挂机策略（2026-10-09 用户设计）：非1v1回合一律空过（不普攻不用技），进1v1后恢复正常；挡刀等反应性决策仍生效
        public int PressureStyle = 0;          // 对赌施压（2026-10-12 用户设计，只换攻击索敌，其余沿用雨露均沾）：0=雨露均沾（默认）1=双边账本索敌
                                               // 模型：每条双边交互是独立账本，gap=我打他的次数-他打我的次数（普攻/军火库/毒药都算投入）。
                                               // 目标：每条账本都领先（投入>被投入）。追平阶段：有账本落后（gap<0）时先追平，两个逆差优先追差距小的；
                                               // 施压阶段：全线不落后时维持最小领先，连击延续（<PressureLeadCap 才续打）落实"连续2回合攻击，他转火即产生逆差"。
        public float PressureStreak = 2f;      // 连击延续加分（仅顺差区、且该账本领先未达 PressureLeadCap 时生效）
        public int PressureLeadCap = 2;        // 单条账本领先达到此值视为已赢下该 1v1，转火下一条最脆弱账本
        public float PressureChaseBonus = 6f;  // 2026-10-19 升级版：追平阶段账本加权（逆差越小越急，+6~+6+cap）；叠在完整 AttackScore 上而非替换
        public float PressureLeadBonus = 3f;   // 2026-10-19 升级版：续压阶段加权（0~+3 按领先幅度线性衰减，账本达标即交还轮转）
        public bool ExorciseWhenSpareMp = true;// 蓝花不完时白嫖驱魔情报
        public float BlindExtortChance = 0.3f; // 盲勒索概率
        public float ExorciseUndeadBonus = 3f; // 驱魔分：威胁 + (目标公开亡灵次数-弃牌堆已阵亡亡灵)×此权重（实验：1/3→+0.025，2→-0.012，均在噪声边缘，取3使有情报时更果断）
        public float WitchHuntIntelBonus = 8f; // 猎巫行动：驱魔索敌时每个"验出女巫"情报的加分（确定处决≈女巫价值8）
        public float BribeEvCoeff = 0.5f;      // 贿赂EV总系数（2026-10-14④，详见 BribeScore）：纯"剩余手牌均值"与普攻EV同档会全场停战瘫痪，0.5=停战+自选牌≈半张牌
        public int BribeMinRemaining = 3;      // ⑧（2026-10-19 用户实战裁定）：给出后剩余非英雄牌 < 此值不贿赂——手牌=肉身+资源，薄了送不起
                                               // （实战回放：AI医师对强攻玩家每回合贿赂，把女巫/牧师/驱魔师真牌一张张送走，低血量时仍在送）
        public float BribeMaxAvg = 5f;         // ⑧：给出后剩余手牌均值 > 此值不贿赂——质量高=没有垃圾可丢，给啥都是真资助敌人（5≈全库均值4.1之上的好手牌线）
        public float CharmRedirectBonus = 6f;  // 美人计侍女贿赂的魅惑价值（2026-10-19 最终版→当晚调参3→6，详见 BribeScore）：攻击重定向到目标走完整伤害判定≈转嫁一次攻击的期望。3 时 AI 使用率仅15.4%（瓶颈不在机制强度——用魅惑的局胜率45.8% vs 不用23.5%）
        public float ColdHuntSealCoeff = 1.4f;  // 冷酷追猎联动（2026-10-19 重做AI同步）：持有者的扣押（警察封印）EV×此系数——被封印的敌牌=回合末免费处决的弹药（"不是自己的警察封的也能吃"，自己的警察封=稳定供弹）
        public float DeathblowGuaranteedBonus = 8f;  // 致命一击联动（2026-10-19 被动版AI同步）：目标HP=1且手牌（不含英雄牌）≤5时我的攻击必中其英雄=锁定淘汰，价值远高于一张牌（淘汰赛淘汰一人≈保底第二+凯旋式滚雪球）
        // 购物车模型（2026-09-30）：每个合法行动算期望收益(EV)入车，每步挑 (EV+顺序加成) 最高的一件结账。
        // 顺序加成(orderBoost)只表达"必须先动"，不虚占收益预算；蓝耗不再扣——合法列表已按MP过滤。
        public float ExtortLimitedCost = 4f;     // 勒索EV = 牌型价值×期望在敌手张数 − 限定技机会成本（整局限一次的军机大臣价值）
        public float SiegeExpireBoost = 10f;     // 攻城锤临期额外普攻的顺序加成（过时不候，提到最高优先级结账）
        public float SeizeOrderBoost = 0.5f;     // 扣押顺序加成：0.8+0.5=1.3，排在普攻(1.0)前，但压不过毒药斩杀(1.8)
        public float HeroPowerOrderBoost = 2f;   // 英雄技顺序加成：police station要在攻击前封被动，save要在连发组合前放
        public float PoliceStationEvPerHit = 1.5f; // police station EV：每点"沉默穿透伤害"的价值（伤害=自身确定输出+其他玩家互相伤害的期望0.5/人）
        public float PoliceStationWasteEv = 1f;    // police station EV：每个被沉默玩家浪费的行动点价值（3蓝只能打1蓝普攻=浪费2点，稳定常数）
        public float PoliceStationCoin = 0.5f;     // 风格0：整局一次的50%硬币决定"用/不用"（让释放时机多样化；也兼作随机对照分组）
        public int PoliceStationStyle = 7;         // 7=最终版默认：风格1打底(留到1v1放)+残血必交(防烂手里)，1500局31.0%/使用率94.1%。6=风格7+爆发窗口50%(用户原案，1500局27.7%暴露掉分主因是提前爆发)。5=爆发50%。1=留到1v1才放。3=爆发必放。4=合并。2=每回合15%。0=旧版早放
        public float PoliceStationTurnChance = 0.15f; // 风格2：每回合15%概率释放（时机多样化）
        public float PoliceStationComboBoost = 100f;  // 风格3/4/5：爆发组合技的顺序加成——购物车选中毒药/军火库时先放沉默（先封后爆，伤害穿透全额兑现）
        public float PoliceStationBurstChance = 0.5f; // 风格5：爆发条件成立时的出手概率（50%出手/50%留招）
        public float PoliceStationHurtMult = 2f;   // 玩家秘籍：上一回合自己受过伤 → EV×此系数（且绕过硬币必放，残血封技能防集火）
        public float SaveEvPerGm = 3f;           // save EV：每张军机大臣牌+3（配多重勒索）
        public float SaveEvPerWitch = 1.5f;      // save EV：每张女巫牌+1.5（配双药连发）
        public float HeroPowerMinEv = 3f;        // 英雄技最低EV门槛：不够赚就不放
        public float AttackEvCoeff = 1f;         // 普攻EV = 全库均值(DeckMean)×此系数；1=一张随机牌的期望价值（2026-10-02 起购物车统一用价值档作单位）
    }

    /// <summary>
    /// AI 行为模块开关：消融实验用。全部开启 = 默认行为（与历史版本决策逐字节一致）。
    /// 关闭某模块时该处回退为"朴素AI"写法（固定选第一个目标/无纪律/随机勒索等），不是简单跳过。
    /// </summary>
    public class AIModules
    {
        public bool RotateFire = true;         // 轮流火力索敌：攻击类行动用独立攻击分（越久没打越优先、本回合没人碰过加分）
        public bool ThreatAssess = true;       // 威胁评估索敌：辅助技（警察/驱魔）/亡语按威胁分选目标
        public bool Grudge = true;             // 记仇：被打累计仇恨，威胁分加权
        public bool ArsenalMemory = true;      // 军火库记忆：见过谁用军火库 → 威胁加分 + 勒索该牌型
        public bool Desperate = true;          // 绝望阎王：自己濒危时威胁公式切换为结仇清算
        public bool SmartExtort = true;        // 勒索纪律：公开信息算期望≥2才动手/军火库牌型/小概率盲勒索
        public bool GuardDiscipline = true;    // 挡刀纪律：只为高价值牌挡刀（关闭=见刀就挡）
        public bool ResourceDiscipline = true; // 资源纪律：毒药 conservative/解药回收/扣押留蓝gate/藏牌阈值/贿赂门槛
        public static readonly AIModules AllOn = new AIModules();
    }

    /// <summary>
    /// 2代AI大脑 v2：在v3.1性格（记仇/军火库记忆/自认最强才斩杀/1v1火力全开/绝望阎王）之上，
    /// 按"牌的预期收益模型"重建经济层：
    ///   - MP预算：1蓝=1次军火库追加攻击是最划算的伤害，优先保证；驱魔/警察只在"蓝花不完"或情报加持时用
    ///   - 一次性资源纪律：女巫双药是整局限一次的爆发，只有斩杀/1v1/自己进斩杀线/残局才动
    ///   - 普攻独立索敌（轮流火力，放弃绝对理性）：攻击类行动不看"谁最威胁"，看"谁最久没被打"——
    ///     最近打过的尽量不打、本回合没人碰过的加分、1v1拉满。没人能靠装死隐身，火力在全场轮转。
    /// AI只使用明面信息+公开事件（OnHostile/OnIntel），不作弊。
    /// </summary>
    public class AIBrain2 : IPlayerBrain2
    {
        public bool IsHuman { get { return false; } }

        readonly Random _rng;
        readonly int _seat;
        readonly AIWeights _w;
        readonly AIModules _mod;
        readonly float[] _grudge;        // 记仇值：谁打过我我记谁
        readonly bool[] _arsenalSeen;    // 军火库记忆：谁用过军火库
        int[] _recentAttacksOn;          // 轮内情报：新一轮开始时清零，别人对index号玩家的攻击次数（本回合是否被碰过）
        int _lastAttackTarget = -1;      // 强攻：自己上次攻击的目标（维持连击）
        readonly int[] _lastAttackedOn;  // 普攻索敌：我上一次攻击index号玩家时的OwnTurnCount（-1=从未打过）
        readonly Dictionary<int, Dictionary<CardType, int>> _intel = new Dictionary<int, Dictionary<CardType, int>>(); // 公开情报：目标手里被验出/封住/挡出/救回的牌型及张数（驱魔/警察/亡灵挡刀/解药/牧师亡语播报）
        int _lastSupport = 0;            // 辅助技轮转（SupportStyle=3）：上次用的是 1=驱魔 2=封印
        readonly int[] _undeadSeen;        // 公开情报：目标手里亡灵被验出/挡出的累计次数（警察/驱魔/挡刀都会播报）
        readonly Dictionary<int, int> _usedCount = new Dictionary<int, int>(); // 本回合每张技能牌已用次数
        int _lastOwnTurn = -1;
        int _incomingOnMe;              // 新一轮内别人打我的次数（我是不是集火目标）
        bool _hurtFlag;                 // 自上一回合以来是否掉过血（NoteHostile置位）
        bool _hurtSinceLastTurn;        // 回合开始时快照：上一回合自己受过伤（police station"受伤后使用"）
        bool? _policeCoinGame;          // police station 风格0整局一次的50%硬币（随机对照分组：用/不用）
        bool _policeTurnPass;           // police station 风格2本回合硬币（15%/回合）
        int[] _duelDealt;               // 对赌施压（PressureStyle=1）双边账本：我对 p 累计投入的攻击资源
        int[] _duelTaken;               // 对赌施压双边账本：p 对我累计投入的攻击资源
        const int MAX_USE_PER_CARD = 2;  // save免费期间也最多连发2次：防止无限驱魔表演

        /// <summary>本局策略名（2026-10-12 策略随机）：开局抽选结果，供 GameBootstrap2 播报与调试。</summary>
        public string StrategyName { get; private set; } = "雨露均沾";
        /// <summary>座位号（策略随机播报用，2026-10-12）。</summary>
        public int Seat { get { return _seat; } }

        /// <summary>
        /// 策略随机开局（2026-10-12 用户设计）：每个 AI 每局独立 50/50 抽选 雨露均沾 / 对赌施压（PressureStyle=1），
        /// 让玩家面对不可预知的策略人格。randSeed 用局种子派生，同种子复盘可复现；不纳入挂机（那是规则测试脚本非策略）。
        /// </summary>
        public static AIBrain2 CreateWithRandomStrategy(int seed, int seat, int playerCount, int randSeed)
        {
            var w = new AIWeights();
            string name = "雨露均沾";
            if (new Random(randSeed).Next(2) == 1) { w.PressureStyle = 1; name = "对赌施压"; }
            var b = new AIBrain2(seed, seat, playerCount, w);
            b.StrategyName = name;
            return b;
        }

        public AIBrain2(int seed, int seat, int playerCount) : this(seed, seat, playerCount, null, null) { }

        public AIBrain2(int seed, int seat, int playerCount, AIWeights weights) : this(seed, seat, playerCount, weights, null) { }

        public AIBrain2(int seed, int seat, int playerCount, AIWeights weights, AIModules modules)
        {
            _rng = new Random(seed);
            _seat = seat;
            _w = weights ?? new AIWeights();
            _mod = modules ?? AIModules.AllOn;
            _grudge = new float[playerCount];
            _arsenalSeen = new bool[playerCount];
            _recentAttacksOn = new int[playerCount];
            _lastAttackedOn = new int[playerCount];
            _undeadSeen = new int[playerCount];
            _duelDealt = new int[playerCount];
            _duelTaken = new int[playerCount];
            for (int i = 0; i < playerCount; i++) _lastAttackedOn[i] = -1;
        }

        /// <summary>订阅引擎 OnHostile：AI只记公开信息，不偷看手牌。</summary>
        public void NoteHostile(int attacker, int victim, ActionKind kind)
        {
            if (kind == ActionKind.ArsenalAttack) _arsenalSeen[attacker] = true;
            if (attacker == _seat) _lastAttackTarget = victim;
            if (attacker != _seat && victim != _seat) _recentAttacksOn[victim]++;
            if (victim == _seat)
            {
                _incomingOnMe++;
                _hurtFlag = true;   // 掉血/被攻击即记伤（police station"上一回合受伤后使用"的输入）
                if (_mod.Grudge) _grudge[attacker] += kind == ActionKind.PoisonAttack ? 2f : 1f;
            }
            // 对赌施压（PressureStyle=1）双边账本：攻击类行动（普攻/军火库/毒药）= 双边投入的资源。
            // 只记公开 OnHostile，与雨露均沾的记仇同源；恐怖分子亡语反击若引擎播报也计入（非自愿投入，两边对称，影响可忽略）。
            if (kind == ActionKind.Attack || kind == ActionKind.ArsenalAttack || kind == ActionKind.PoisonAttack)
            {
                if (attacker == _seat) _duelDealt[victim]++;
                else if (victim == _seat) _duelTaken[attacker]++;
            }
        }

        /// <summary>订阅引擎 OnIntel：公开牌型情报（驱魔抽出/处决、警察扣押命中、亡灵挡刀、解药/牧师亡语回收），全员可记，按张数累计。</summary>
        public void NoteIntel(int actorSeat, int targetSeat, CardType seen)
        {
            if (!_intel.TryGetValue(targetSeat, out var set)) { set = new Dictionary<CardType, int>(); _intel[targetSeat] = set; }
            set[seen] = set.TryGetValue(seen, out var n2) ? n2 + 1 : 1;
            if (seen == CardType.Undead && targetSeat >= 0 && targetSeat < _undeadSeen.Length) _undeadSeen[targetSeat]++;
        }

        public float GrudgeOf(int seat) { return _grudge[seat]; }
        public bool KnowsArsenal(int seat) { return _arsenalSeen[seat]; }

        // 调试用（StatPolice 读）：police station 本局硬币结果 / 首次决策时是否带伤
        public bool DbgPoliceCoin { get { return _policeCoinGame ?? true; } }
        public bool DbgHurt { get { return _hurtSinceLastTurn; } }
        public bool DbgFirstCoinHurt { get { return _dbgFirstCoinHurt; } }
        public double DbgCoinRaw { get { return _dbgCoinRaw; } }
        bool _dbgFirstCoinHurt;
        double _dbgCoinRaw = -1;

        // ------------------------------------------------------------------
        // 主决策
        // ------------------------------------------------------------------

        public GameAction ChooseAction(GameEngine2 g, int seat, List<GameAction> legal)
        {
            var me = g.Players[seat];
            if (me.OwnTurnCount != _lastOwnTurn)
            {
                _usedCount.Clear(); _lastOwnTurn = me.OwnTurnCount; _recentAttacksOn = new int[g.Players.Count]; _incomingOnMe = 0;
                _hurtSinceLastTurn = _hurtFlag; _hurtFlag = false;   // 快照：上一回合是否受伤
                _policeTurnPass = _rng.NextDouble() < _w.PoliceStationTurnChance;   // 风格2：本回合15%硬币
            }
            legal = legal.Where(a => a.SourceCardId < 0 || !_usedCount.TryGetValue(a.SourceCardId, out var n) || n < MAX_USE_PER_CARD).ToList();
            var act = ChooseActionCore(g, seat, legal, me);
            if (act.SourceCardId >= 0) _usedCount[act.SourceCardId] = _usedCount.TryGetValue(act.SourceCardId, out var m) ? m + 1 : 1;
            if (act.Kind == ActionKind.Exorcise) _lastSupport = 1;        // 辅助技轮转（SupportStyle=3）记忆
            else if (act.Kind == ActionKind.Seize) _lastSupport = 2;
            if (act.Kind == ActionKind.Attack || act.Kind == ActionKind.ArsenalAttack || act.Kind == ActionKind.PoisonAttack)
                _lastAttackedOn[act.TargetSeat] = me.OwnTurnCount;   // 普攻索敌：记下本次攻击目标（轮流火力依据）
            return act;
        }

        GameAction ChooseActionCore(GameEngine2 g, int seat, List<GameAction> legal, PlayerState me)
        {
            int aliveOpponents = g.Players.Count(x => x.Alive && x.Seat != seat);
            bool duel = aliveOpponents == 1;
            // 开局调度阶段（2026-10-16 调度券）：贪心换掉"价值最低且低于全库均值"的非英雄牌——
            // 每次换牌期望收益=牌堆均值−被换牌价值>0 才动手；没有可赚的就收工。
            // （炉石原教旨是"换费用过高的牌"；本游戏共用牌库+分值差，换低分牌≈抬手牌分、牌库分被稀释，见文档。）
            if (legal.Any(a => a.Kind == ActionKind.Mulligan || a.Kind == ActionKind.EndMulligan))
            {
                // 3代开局调度（2026-10-21 用户裁定）：开局就考虑法术种类与数量——研习要吃技能点，
                // 手里法术越多研习选择越足（"能有3张太好了"）；牌库 15/62≈24% 是法术，换掉非法术牌≈0.24张法术期望。
                // 因此：法术<3张时优先换"价值最低的非法术牌"（哪怕价值不低于 DeckMean，追法术密度的期望大于单张贬值），
                // 但6分以上的牌（牧师/女武神/带刀侍卫/军机大臣…）绝不换——追法术不拆骨架。
                var g3m = g as GameEngine3;
                if (g3m != null)
                {
                    int spells = me.Hand.Count(c => GameEngine3.IsSpellType(c.Type));
                    if (spells < 3)
                    {
                        var dump = legal.Where(a => a.Kind == ActionKind.Mulligan)
                            .Select(a => new { a, c = me.Hand.First(x => x.Id == a.SourceCardId) })
                            .Where(x => !GameEngine3.IsSpellType(x.c.Type) && KeepValue(x.c.Type) <= 5)
                            .OrderBy(x => KeepValue(x.c.Type)).FirstOrDefault();
                        if (dump != null) return dump.a;
                    }
                }
                // 3代：通用贪心分支不再拿法术牌当"最差牌"换走（法术已被上方专项处理，这里排外保底）
                var pool = legal.Where(a => a.Kind == ActionKind.Mulligan);
                if (g3m != null)
                    pool = pool.Where(a => !GameEngine3.IsSpellType(me.Hand.First(c => c.Id == a.SourceCardId).Type));
                var worst = pool
                    .Select(a => new { a, v = (float)KeepValue(me.Hand.First(c => c.Id == a.SourceCardId).Type) })
                    .OrderBy(x => x.v).FirstOrDefault();
                if (worst != null && worst.v < DeckMean) return worst.a;
                return legal.First(a => a.Kind == ActionKind.EndMulligan);
            }
            // 挂机策略（2026-10-09 用户设计）：非1v1 回合一律空过（不普攻不用技），进 1v1 后恢复正常思考。
            // 反应性决策不受影响：挡刀（ChooseGuardIntercept）与购物车内置的响应仍生效。
            if (_w.AfkUntilDuel && aliveOpponents > 1) return legal.First(a => a.Kind == ActionKind.EndTurn);
            var cart = new List<CartItem>();
            // 普攻 EV = 全库均值 × 系数：2026-10-02 起购物车统一用"价值档"作单位（与 KeepValue/勒索/藏牌/解药同尺度）。
            float atkEv = DeckMean * _w.AttackEvCoeff;

            // 购物车模型：每个合法行动先算期望收益(EV)入车，每步挑 (EV+顺序加成) 最高的一件去"收银台"执行；
            // 执行后下一拍全量重算（蓝/已用标记/手牌都变了），因此收益判定与执行顺序半解耦——
            // orderBoost 只表达"必须先动"（先封后打/过时不候限定技/英雄技前置），不让它虚占收益预算。

            // 0. 英雄技：save 给连发组合前置（GM多重勒索/女巫双药），police station 在攻击前封全场（沉默让本回合伤害穿透）
            foreach (var power in legal.Where(a => a.Kind == ActionKind.HeroPower))
            {
                float ev = HeroPowerEv(g, seat, me, duel, power);
                if (ev < _w.HeroPowerMinEv) continue;
                // police station 释放策略：
                // 风格7（最终版默认，1500局31.0%/使用率94.1%）：风格1打底 + 残血必交（HP≤1直接交，防烂手里）。
                // 风格6（用户原案，1500局27.7%）：风格7 + 1v1前爆发窗口(购物车选中毒药/军火库)50%考虑出手——
                //   数据证明提前爆发是掉分主因，故默认不切风格6，保留可切换。
                // 风格5：爆发条件成立时50%概率先封后爆，不中则留招。
                // 风格4=风格3+风格1合并：1v1前只在购物车选中毒药/军火库时先封后爆，进1v1后自动切为留招必放。
                // 风格3：爆发组合技——购物车建好后偷看：若本回合计划是毒药/军火库，先放沉默再爆发（后处理，见收银台前）。
                // 风格2：每回合15%概率放（时机多样化）。
                // 风格1：留到有玩家阵亡进入1v1才放。
                // 风格0（旧）：整局一次50%硬币 + 受伤后EV翻倍。
                if (me.HeroId == 1)
                {
                    if (_w.PoliceStationStyle == 6)
                    {
                        if (me.HP <= 1)   // 补充②：残血直接交（最高优先级，防整局烂手里）
                        {
                            cart.Add(new CartItem(power, ev, _w.PoliceStationComboBoost));
                            continue;
                        }
                        if (aliveOpponents > 1) continue;   // 非1v1：交给后面的爆发偷看（补充①）
                        // 1v1：常规放行（风格1打底）
                    }
                    else if (_w.PoliceStationStyle == 7)   // 风格7：风格1打底 + 残血必交（无爆发窗口，对照组：验证拖后腿的是不是爆发补充）
                    {
                        if (me.HP <= 1)
                        {
                            cart.Add(new CartItem(power, ev, _w.PoliceStationComboBoost));
                            continue;
                        }
                        if (aliveOpponents > 1) continue;
                    }
                    else if (_w.PoliceStationStyle == 1 || _w.PoliceStationStyle == 4)
                    {
                        if (aliveOpponents > 1) continue;   // 非1v1：风格1不放；风格4交给后面的爆发偷看
                    }
                    else if (_w.PoliceStationStyle == 3 || _w.PoliceStationStyle == 5)
                    {
                        continue;   // 风格3/5在购物车建完后统一处理（先决策后执行）
                    }
                    else if (_w.PoliceStationStyle == 2)
                    {
                        if (!_policeTurnPass) continue;
                    }
                    else
                    {
                        if (!_policeCoinGame.HasValue)
                        {
                            double raw = _rng.NextDouble();
                            _dbgCoinRaw = raw;
                            _policeCoinGame = raw < _w.PoliceStationCoin;
                            _dbgFirstCoinHurt = _hurtSinceLastTurn;   // 首次决策时的对照组成因
                        }
                        if (!_policeCoinGame.Value) continue;
                    }
                }
                cart.Add(new CartItem(power, ev, _w.HeroPowerOrderBoost));
            }

            // 1. 解药：EV=弃牌堆可回收最高价值（仅限上一回合内阵亡的，2026-09-30）；平衡轴=女巫自身生存压力（残血/被集火→价值标准放宽）
            //    牺牲专属（2026-10-03）：自己是牺牲英雄且上回合有牧师死亡时，解药门槛降低——
            //    牧师是牺牲玩家的挡刀资源，救回来等于续挡刀弹药（只影响牺牲玩家，其他英雄不解锁）
            var ant = legal.FirstOrDefault(a => a.Kind == ActionKind.Antidote);
            if (ant != null && _mod.ResourceDiscipline)
            {
                int bestVal = g.Discard.Where(c => c.Type.IsRole() && c.Type != CardType.Hero && c.DiedStamp >= me.LastTurnStamp)
                                       .Select(c => KeepValue(c.Type)).DefaultIfEmpty(0).Max();
                bool pressured = me.HP <= 1 || _incomingOnMe >= 2;
                int need = pressured ? _w.AntidoteValueMinPressured : _w.AntidoteValueMin;
                if (g.HeroOf(seat).PriestSacrifice
                    && g.Discard.Any(c => c.Type == CardType.Priest && c.DiedStamp >= me.LastTurnStamp))
                    need = Math.Max(1, need - _w.PriestAntidoteBoost);
                if (bestVal >= need)
                    cart.Add(new CartItem(ant, bestVal, 0));
            }

            // 2. 带刀侍卫布防：布防单位=侍卫+大臣+带刀侍卫+守卫（2026-10-02 规则扩大），够2个单位就 buff（亡语反击威慑）
            var arm = legal.FirstOrDefault(a => a.Kind == ActionKind.ArmGuard);
            if (arm != null && me.Hand.Count(c => c.Type == CardType.Guard || c.Type == CardType.Minister
                                              || c.Type == CardType.ArmedGuard || c.Type == CardType.Warden) >= _w.ArmGuardMinBodies)
                cart.Add(new CartItem(arm, atkEv * 1.5f, 0));

            // 2.5 恐怖分子炸弹（2026-10-13 改版）：EV=敌方存活英雄数×每点伤害价值（全场AOE）。
            // 爆弹会送掉恐怖分子本体（≈3价值档），1v1时净收益偏薄，至少2个敌人才安放。
            var bomb = legal.FirstOrDefault(a => a.Kind == ActionKind.BombPlant);
            if (bomb != null && aliveOpponents >= 2)
                cart.Add(new CartItem(bomb, atkEv * 1.2f, 0));

            // 3. 扣押：先封后打——顺序加成让它排在普攻前，但 EV modest（0.8×普攻）不占收益预算
            var sez = legal.Where(a => a.Kind == ActionKind.Seize).ToList();
            if (sez.Count > 0 && (!_mod.ResourceDiscipline || me.MP >= GameEngine2.COST_BASIC_ATTACK + GameEngine2.COST_SEIZE))
                // 冷酷追猎（2026-10-19 重做）：持有者封印EV×ColdHuntSealCoeff——被封印的敌牌是回合末免费处决的弹药
                cart.Add(new CartItem(PickAttackTarget(sez, g, me),
                    atkEv * 0.8f * (me.HeroId == 9 ? _w.ColdHuntSealCoeff : 1f), _w.SeizeOrderBoost));

            // 4. 普攻：EV=全库均值×系数（一张随机牌的期望价值）。
            //    本回合已普攻过而 Attack 仍在合法列表 → 必是攻城锤前2回合的额外普攻（普通攻击已被 BasicAttackUsed 挡掉），
            //    过时不候 → 顺序加成提到最高，保证当拍必打。
            //    攻城锤前2回合的首次普攻同样挂最高加成：先锁死"普攻+额外普攻"的2点蓝，防止被扣押/驱魔/勒索等
            //    先发技能把蓝花光、额外普攻活活饿死（修复AI有蓝却在前两回合浪费额外攻击的问题）。
            var atk = legal.Where(a => a.Kind == ActionKind.Attack).ToList();
            if (atk.Count > 0)
            {
                float boost = 0f;
                if (g.BasicAttackUsed) boost = _w.SiegeExpireBoost;
                else if (me.HeroId == 3 && g.SiegeExtraLive(seat) && me.MP >= 2 * GameEngine2.COST_BASIC_ATTACK)
                    boost = _w.SiegeExpireBoost;
                cart.Add(new CartItem(PickAttackTarget(atk, g, me), atkEv, boost));
            }

            // 5. 军火库：EV=0.75×普攻——伤害同为1张牌，但会暴露自己持有军火库（+被侍女格挡的风险）
            var ars = legal.Where(a => a.Kind == ActionKind.ArsenalAttack).ToList();
            if (ars.Count > 0)
                cart.Add(new CartItem(PickAttackTarget(ars, g, me), atkEv * 0.75f, 0));

            // 6. 毒药：斩杀判定与普攻共用同一套索敌——不问"该不该用毒"，问"索敌选中的目标值不值得毒"。
            //    条件不成立不入车（整局限一次的爆发不随便花），成立则 EV=1.8×普攻 压过普攻。
            var poi = legal.Where(a => a.Kind == ActionKind.PoisonAttack).ToList();
            if (poi.Count > 0)
            {
                var bestPoi = PickAttackTarget(poi, g, me);
                var poiTarget = g.Players[bestPoi.TargetSeat];
                bool guardNet = me.Hand.Any(c => c.Type == CardType.Guard);
                bool usePoison = duel
                    || poiTarget.HP <= _w.PoisonKillHp
                    || g.TurnNumber >= _w.PoisonLateTurn
                    || (!guardNet && SelfInKillLine(g, seat, me));
                if (usePoison) cart.Add(new CartItem(bestPoi, atkEv * 1.8f, 0));
            }

            // 7. 驱魔：EV=情报收益——有公开情报（亡灵被验出；猎巫行动额外认女巫情报=确定处决）1.5×普攻优先动；蓝花不完时白嫖情报(0.5×普攻)
            var exo = legal.Where(a => a.Kind == ActionKind.Exorcise).ToList();
            if (exo.Count > 0)
            {
                var best = exo.OrderByDescending(a => ExorciseScore(g, me, g.Players[a.TargetSeat])).First();
                bool informed = _intel.Values.Any(s => s.ContainsKey(CardType.Undead))
                                || _undeadSeen.Any(v => v > 0)
                                || (me.HeroId == 5 && _intel.Values.Any(s => s.ContainsKey(CardType.Witch)));
                if (informed || !_mod.ResourceDiscipline) cart.Add(new CartItem(best, atkEv * 1.5f, 0));
                else if (_w.ExorciseWhenSpareMp && me.MP >= GameEngine2.COST_EXORCISE) cart.Add(new CartItem(best, atkEv * 0.5f, 0));
            }

            // 测试任务1（2026-10-04 用户设计）：封印vs驱魔优先级风格——+5 顺序加成只改变两者先后，不扰动其他行动的预算
            if (_w.SupportStyle != 0)
            {
                int sezIdx = cart.FindIndex(c => c.Act.Kind == ActionKind.Seize);
                int exoIdx = cart.FindIndex(c => c.Act.Kind == ActionKind.Exorcise);
                if (sezIdx >= 0 && exoIdx >= 0)
                {
                    if (_w.SupportStyle == 1) cart[exoIdx].Boost += 5f;                 // 驱魔总是优于封印
                    else if (_w.SupportStyle == 2) cart[sezIdx].Boost += 5f;          // 封印总是优于驱魔
                    else if (_w.SupportStyle == 3)                                      // 轮转：上次封印这次驱魔，反之亦然
                    {
                        if (_lastSupport == 2) cart[exoIdx].Boost += 5f;
                        else if (_lastSupport == 1) cart[sezIdx].Boost += 5f;
                        else cart[exoIdx].Boost += 5f;                                  // 首次：驱魔先行
                    }
                }
            }

            // 8. 勒索：EV=牌型价值×期望在敌手张数 − 限定技机会成本；期望渺茫(EV≤0)不入车。
            //    斩杀清场：所有对手都只剩1HP → 勒索英雄 EV=100 直接清场。
            var ext = legal.Where(a => a.Kind == ActionKind.Extort).ToList();
            if (ext.Count > 0)
            {
                var opps = g.Players.Where(x => x.Alive && x.Seat != seat).ToList();
                if (opps.Count > 0 && opps.All(x => x.HP <= 1))
                {
                    var heroExt = ext.FirstOrDefault(a => a.TargetType == CardType.Hero);
                    if (heroExt != null) cart.Add(new CartItem(heroExt, 100f, 0));
                }
                if (_mod.SmartExtort)
                {
                    foreach (var a in ext)
                    {
                        int expect = ExpectedInEnemyHands(g, me, a.TargetType);
                        float ev = KeepValue(a.TargetType) * expect - _w.ExtortLimitedCost;
                        if (ev > 0) cart.Add(new CartItem(a, ev, 0));
                    }
                    // 小概率盲勒索一个4张的牌型
                    if (_rng.NextDouble() < _w.BlindExtortChance)
                    {
                        var four = ext.Where(a => a.TargetType == CardType.Undead || a.TargetType == CardType.Exorcist
                                               || a.TargetType == CardType.Priest || a.TargetType == CardType.Police
                                               || a.TargetType == CardType.Guard || a.TargetType == CardType.Minister).ToList();
                        if (four.Count > 0) cart.Add(new CartItem(four[_rng.Next(four.Count)], atkEv * 0.5f, 0));
                    }
                }
                else if (_rng.NextDouble() < 0.3)
                    cart.Add(new CartItem(ext[_rng.Next(ext.Count)], atkEv * 0.5f, 0));   // 无纪律：30% 闭眼随机勒索
            }

            // 9. 守卫护牌：EV=被护牌价值 − 护牌成本(4价值档)，≥5价值的牌才值得护（2026-10-15 改版："无法选取"状态）
            var hides = legal.Where(a => a.Kind == ActionKind.Hide).ToList();
            if (hides.Count > 0)
            {
                if (!_mod.ResourceDiscipline) cart.Add(new CartItem(hides[0], atkEv * 3f, 0));   // 无纪律：闭眼藏第一张
                else
                {
                    var bestHide = hides.OrderByDescending(a => ValueOf(me.Hand.First(c => c.Id == a.SecondCardId))).First();
                    float hv = ValueOf(me.Hand.First(c => c.Id == bestHide.SecondCardId));
                    if (hv >= 5) cart.Add(new CartItem(bestHide, hv - 4, 0));
                }
            }

            // 10. 贿赂（2026-10-14③ 用户重设计）：**无前置条件，纯收益判定**——贿赂=把用不上的牌当"自选被打的牌"丢出去换停战，
            // 防不了集火（只能买一家），所以"被集火才贿赂"的门禁是错误的（集火是后置现象，被集火感不可检测）。
            // EV = 给出后剩余手牌的价值均值（丢垃圾抬均值；给出好牌=资助敌人，均值自降）；
            // 安放完炸弹的恐怖分子按 0 分计（敌人最终什么都得不到）；军机大臣先给后勒索另有大涨窗口。
            {
                var bribes = legal.Where(a => a.Kind == ActionKind.Bribe).ToList();
                if (bribes.Count > 0)
                {
                    if (!_mod.ResourceDiscipline) cart.Add(new CartItem(bribes[0], atkEv * 2f, 0));   // 无纪律：闭眼送第一张
                    else
                    {
                        var bestBribe = bribes.Select(a => new CartItem(a,
                                BribeScore(g, me, g.Players[a.TargetSeat], me.Hand.First(c => c.Id == a.SecondCardId)), 0))
                            .OrderByDescending(c => c.Ev).First();
                        if (bestBribe.Ev > 0) cart.Add(bestBribe);
                    }
                }
            }

            // 10.5 合成（2026-10-06）：EV=KeepValue(合成牌) + DeckMean(奖励抽1张) − ΣOwnCardValue(配方牌)；正收益才入车。
            // 2026-10-13 用户调价：① 消耗的 1 行动点不再额外扣分（MP 机会成本交给购物车竞争隐式处理）；
            // ② 审判官 += 外面期望女巫数×1分（仿军机大臣勒索猜测法：牌库3张−弃牌堆−自己手牌）；③ 猎巫行动英雄(HeroId 5)不合成审判官（自带处决女巫，审判官冗余）。
            // 女祭司配方里的女巫走 OwnCardValue→ValueOf 动态价（双药8/单药5/药尽2）——女巫用药贬到2分后女祭司 EV 转正，这是本调价的联动设计。
            var crafts = legal.Where(a => a.Kind == ActionKind.Craft).ToList();
            if (crafts.Count > 0)
            {
                foreach (var a in crafts)
                {
                    if (a.TargetType == CardType.Inquisitor && me.HeroId == 5) continue;   // 猎巫行动英雄：审判官冗余
                    var cost = g.CraftCost(seat, a.TargetType, a.SourceCardId);   // "任意"位按合法行动指定的牌估价（便宜的第三张EV更高，自然入选）
                    if (cost == null) continue;
                    // 合成奖励按座位方案计：0=抽1张牌（全库均值）/ 2=方案C护盾。
                    // 护盾EV=合成后手牌均值（2026-10-14 用户口径）：护盾吞掉的那次攻击本该抽走自己手牌随机一张，
                    // 保护对象=自己手牌（含英雄——英雄被抽中=1伤害，按全库均值平价计入；不设折扣倍率：
                    // 窗口内94%触发已实证，且无消耗/无暴露/不可被针对，仿军火库的0.75在这里没有机制基础）。
                    float rewardEv;
                    if (g.CraftRewardStyle[seat] == 2)
                    {
                        float postSum = me.Hand.Sum(x => (float)(x.Type == CardType.Hero ? DeckMean : KeepValue(x.Type)))
                                      - cost.Sum(c => (float)KeepValue(c.Type))
                                      + KeepValue(a.TargetType);
                        float postCount = me.Hand.Count - cost.Count + 1;   // -3配方 +1合成牌
                        rewardEv = postSum / postCount;
                    }
                    else rewardEv = DeckMean;
                    float ev = KeepValue(a.TargetType) + rewardEv - cost.Sum(c => (float)OwnCardValue(c, me));
                    if (a.TargetType == CardType.Inquisitor)
                        ev += ExpectedInEnemyHands(g, me, CardType.Witch) + ExpectedInEnemyHands(g, me, CardType.Undead);   // 外面每个女巫/亡灵各+1分（2026-10-13 用户：审判官2HP身体强化捉鬼任务）
                    if (!_mod.ResourceDiscipline) ev = Math.Max(ev, 2f);   // 无纪律：保底当成好事做
                    if (ev > 0) cart.Add(new CartItem(a, ev, 0));
                }
            }

            // 10.6 合成牌技能：女祭司双限定技 / 典狱长扣押 / 审判官审判
            var srv = legal.FirstOrDefault(a => a.Kind == ActionKind.SynthRevive);
            if (srv != null)
            {
                int bestVal = g.Discard.Where(c => c.Type.IsRole() && c.Type != CardType.Hero)
                                       .Select(c => KeepValue(c.Type)).DefaultIfEmpty(0).Max();
                if (bestVal >= 5) cart.Add(new CartItem(srv, bestVal - 1f, 0));   // 限定技：只捞高价值
            }
            var sp = legal.FirstOrDefault(a => a.Kind == ActionKind.SynthPrayer);
            if (sp != null && me.HP < me.MaxHP)
                cart.Add(new CartItem(sp, 1.5f, 0));   // 延迟2回合的回血，满血不放
            var hos = legal.Where(a => a.Kind == ActionKind.SynthHostage).ToList();
            if (hos.Count > 0 && (!_mod.ResourceDiscipline || me.MP >= GameEngine2.COST_BASIC_ATTACK + GameEngine2.COST_SYNTH_SKILL))
                cart.Add(new CartItem(PickAttackTarget(hos, g, me), atkEv * 0.7f, 0));   // 人质=偷1张牌+封印1回合
            var jud = legal.Where(a => a.Kind == ActionKind.SynthJudge).ToList();
            if (jud.Count > 0)
                cart.Add(new CartItem(PickAttackTarget(jud, g, me), atkEv * 0.8f, 0));   // 审判≈驱魔的下位替代（只处决异端）

            // 10.7 3代·法术迸发（2026-10-21 用户裁定；2026-10-22 中午炼金术重做并入）：learn 与施法入车。
            // learn：只学手里持有的法术（抽不到的学了也放不出），EV=该法术释放收益−1（learn 本身耗1行动点）；
            // 施法收益（用户口径）：屏障固定5；清晰术固定6；
            // 雷霆按"能对目标造成的伤害点数"查表：1点=1分、2点=6分、3点=11分（每多1点+5分）；
            // 点燃=目标手里有军火库8分、否则2分；
            // 炼金术（2026-10-22 中午重做=通用法术）：合法行动 TargetType=被模仿的已研习法术，
            // 直接命中下面各 case 按真法术计价——直接释放无延期折扣（"贵的东西缺点只有贵"）。
            var g3 = g as GameEngine3;
            if (g3 != null)
            {
                foreach (var a in legal.Where(x => x.Kind == ActionKind.Learn))
                {
                    if (!me.Hand.Any(c => c.Type == a.TargetType)) continue;   // 手里没这张法术牌，学了也白学
                    // 炼金术是通用法术：先学其它法术再学它，否则无物可模仿（引擎同样禁止空手释放）
                    if (a.TargetType == CardType.Alchemy
                        && !GameEngine3.SpellTypes.Any(t => t != CardType.Alchemy && g3.HasSpell(seat, t))) continue;
                    float learnEv = SpellLearnEv(a.TargetType) - GameEngine3.COST_LEARN;
                    if (learnEv > 0) cart.Add(new CartItem(a, learnEv, 0));
                }
                foreach (var a in legal.Where(x => x.Kind == ActionKind.CastSpell))
                {
                    float castEv = SpellCastEv(g3, a);
                    if (castEv > 0) cart.Add(new CartItem(a, castEv, 0));
                }
            }

            // 收银台：购物车为空才空过；否则挑 EV+orderBoost 最高的一件结账
            // police station 风格3/4/5/6（爆发组合技，先决策后执行）：购物车已建好——偷看本回合计划，
            // 若选中的最高优先级行动是毒药/军火库（本回合要爆发），则先放沉默（最高顺序加成）再爆发：
            // 对方被沉默后不能挡刀、亡灵免疫/牧师亡语失效，爆发的每点伤害都穿透兑现。
            // 风格4仅在非1v1走这里（1v1已在上面的常规入口放行）；风格5/6在爆发条件上加50%出手硬币（不中则留招）。
            if (me.HeroId == 1 && (_w.PoliceStationStyle == 3 || _w.PoliceStationStyle == 5 || _w.PoliceStationStyle == 6
                || (_w.PoliceStationStyle == 4 && aliveOpponents > 1)))
            {
                var power3 = legal.FirstOrDefault(a => a.Kind == ActionKind.HeroPower);
                if (power3 != null)
                {
                    float ev3 = HeroPowerEv(g, seat, me, duel, power3);
                    if (ev3 >= _w.HeroPowerMinEv && cart.Count > 0)
                    {
                        var peek = cart.OrderByDescending(c => c.Ev + c.Boost).First();
                        if (peek.Act.Kind == ActionKind.ArsenalAttack || peek.Act.Kind == ActionKind.PoisonAttack)
                        {
                            bool fire = (_w.PoliceStationStyle == 5 || _w.PoliceStationStyle == 6)
                                ? _rng.NextDouble() < _w.PoliceStationBurstChance   // 风格5/6：爆发窗口50%出手
                                : true;                                             // 风格3/4：爆发必放
                            if (fire) cart.Add(new CartItem(power3, ev3, _w.PoliceStationComboBoost));
                        }
                    }
                }
            }
            if (cart.Count == 0) return legal.First(a => a.Kind == ActionKind.EndTurn);
            return cart.OrderByDescending(c => c.Ev + c.Boost).First().Act;
        }

        /// <summary>3代法术的研习价值基准（2026-10-21 用户口径，与 SpellCastEv 同源；learn 入车时再扣1行动点成本）。
        /// 2026-10-22 中午炼金术重做：通用法术位，研习价=5（配合先学其它法术的门禁，net 4）。</summary>
        static float SpellLearnEv(CardType t)
        {
            switch (t)
            {
                case CardType.Ignite: return 6f;
                case CardType.Thunder: return 6f;
                case CardType.Clarity: return 6f;
                case CardType.Barrier: return 5f;
                case CardType.Comet: return 5f;   // 2026-10-21 用户裁定：彗星也是法术牌，反制需研习——给反制潜力一个中位基准价（可调）
                case CardType.Alchemy: return 5f; // 2026-10-22 重做：泛用复制位，net learn EV=4（可调）
                default: return 0f;
            }
        }

        /// <summary>3代法术施法 EV（2026-10-21 用户口径；2026-10-22 中午炼金术重做并入）：
        /// 屏障固定5；清晰术固定6；
        /// 雷霆按"能对目标造成的伤害点数"查表：1点=1分、2点=6分、3点=11分（每多1点+5分）；
        /// 点燃：目标手牌里有军火库=8分，否则2分；
        /// 炼金术=通用法术：合法行动的 TargetType 已是被模仿法术，天然命中对应 case 按真法术计价（无折扣）。</summary>
        static float SpellCastEv(GameEngine3 g3, GameAction a)
        {
            switch (a.TargetType)
            {
                case CardType.Barrier: return 5f;
                case CardType.Clarity: return 6f;
                case CardType.Ignite:
                    return g3.Players[a.TargetSeat].Hand.Any(c => c.Type == CardType.Arsenal) ? 8f : 2f;
                case CardType.Thunder:
                    {
                        int times = (int)Math.Round(g3.Players[a.TargetSeat].Hand.Count * 0.25, MidpointRounding.AwayFromZero);
                        return times <= 0 ? 0f : 5f * times - 4f;
                    }
                default: return 0f;   // 炼金术的合法行动 TargetType=被模仿法术，已在上面各 case 计价
            }
        }

        /// <summary>购物车条目：Act=行动，Ev=期望收益（张牌为单位），Boost=顺序加成（只决定先后，不虚增收益）。</summary>
        class CartItem
        {
            public readonly GameAction Act;
            public readonly float Ev;
            public float Boost;   // 可写：SupportStyle 在入车后微调封印/驱魔的相对顺序
            public CartItem(GameAction act, float ev, float boost) { Act = act; Ev = ev; Boost = boost; }
        }

        /// <summary>
        /// 英雄技EV（主动技）：
        /// police station（2026-10-02 用户设计）= 沉默穿透伤害×参数 + 被浪费AP——
        ///   收益不取决于封住多少张手牌，而取决于：①对方因沉默不能用技能（挡刀/亡灵免疫/牧师亡语失效）而多吃的伤害——
        ///   自己本回合能打出的伤害是确定的（基础普攻1，攻城锤前2回合双普攻/手里有军火库且蓝够各再+1），
        ///   其他玩家互相打的伤害不可预测→按基础攻击1×"不攻击我"的概率1/2（3人局，玩家不攻击自己）折算；
        ///   ②对方被沉默后3蓝只能打1蓝普攻，浪费的行动点每人稳定2点。
        ///   附加玩家秘籍：上一回合自己受过伤 → EV×受伤加成（残血封技能防集火）。
        /// save = 手里军机大臣×3 + 女巫×1.5（连发发动机）。
        /// 两者都需 ≥ HeroPowerMinEv 才入车。
        /// </summary>
        float HeroPowerEv(GameEngine2 g, int seat, PlayerState me, bool duel, GameAction act)
        {
            if (me.HeroId == 1)   // police station
            {
                int others = g.Players.Count(p => p.Alive && p.Seat != seat);
                float ownPower = 1f;
                if (g.SiegeExtraLive(seat) && me.MP >= 2 * GameEngine2.COST_BASIC_ATTACK) ownPower += 1f;
                if (me.Hand.Any(c => c.Type == CardType.Arsenal) && me.MP >= GameEngine2.COST_BASIC_ATTACK + GameEngine2.COST_ARSENAL) ownPower += 1f;
                float ev = (ownPower + others * 0.5f) * _w.PoliceStationEvPerHit + others * 2f * _w.PoliceStationWasteEv;
                if (_hurtSinceLastTurn) ev *= _w.PoliceStationHurtMult;
                return ev;
            }
            if (me.HeroId == 2)   // save
                return me.Hand.Count(c => c.Type == CardType.GrandMinister) * _w.SaveEvPerGm
                     + me.Hand.Count(c => c.Type == CardType.Witch) * _w.SaveEvPerWitch;
            return 0f;
        }

        /// <summary>牌型期望在敌手的张数（公开信息：牌库构成-弃牌堆-自己手牌）。英雄牌不参与此计算。</summary>
        static int ExpectedInEnemyHands(GameEngine2 g, PlayerState me, CardType t)
        {
            int total;
            switch (t)
            {
                case CardType.Undead: case CardType.Exorcist: case CardType.Priest:
                case CardType.Police: case CardType.Guard: case CardType.Minister:
                    total = 4; break;
                case CardType.Witch: case CardType.Warden: total = 3; break;
                case CardType.Arsenal: case CardType.ArmedGuard: case CardType.GrandMinister: total = 1; break;
                default: return 0;   // 英雄：走斩杀线逻辑，不按期望
            }
            return total - g.Discard.Count(c => c.Type == t) - me.Hand.Count(c => c.Type == t);
        }

        /// <summary>牌的价值（看牌面状态）：女巫按剩余药数贬值，其余按类型档位。</summary>
        static int ValueOf(Card c)
        {
            if (c.Type == CardType.Witch)
            {
                int left = (c.PoisonUsed ? 0 : 1) + (c.AntidoteUsed ? 0 : 1);
                return left == 2 ? 8 : left == 1 ? 5 : 2;   // 双药8 / 单药5 / 药尽2（随 KeepValue 女巫档同步）
            }
            // 2026-10-13 用户 combo 设计：倒数中的恐怖分子对我方价值≈0——炸弹以安放者敌我划线，
            // 爆炸时本体从任何位置被移除（同归于尽），必死之牌不值得挡/回收/可惜；贿赂送人=纯赚停战。
            if (c.Type == CardType.Terrorist && c.BombActive) return 0;
            return KeepValue(c.Type);
        }

        /// <summary>自己手牌中某张牌的"持有价值"（2026-10-13 用户定价）：① 工人 MP+1 不叠加——第一张工人=效果价 4，
        /// 后续工人=肉身价 3；② 倒数中的恐怖分子按肉身价 3 计——全局 ValueOf 的 0 分会让合成配方白吃这张牌，
        /// 打断"留给大臣贿赂"的 combo（用户 2026-10-13 裁定：合成不许截胡贿赂的物料）。
        /// 用于给出/消耗自己牌的决策（贿赂/合成）；对手手牌估值仍用静态档。</summary>
        int OwnCardValue(Card c, PlayerState me)
        {
            if (c.Type == CardType.Worker && me.Hand.Any(x => x.Type == CardType.Worker && x.Id != c.Id))
                return 3;   // 非首张工人：MP 不叠加，只值肉身价
            if (c.Type == CardType.Terrorist && c.BombActive)
                return KeepValue(c.Type);   // 倒数中的恐怖分子：消耗决策按肉身价，不许当免费配方
            return ValueOf(c);
        }

        /// <summary>自己进了对手的斩杀线（谁都能一两轮拍死我）：留着资源也是死，全花。</summary>
        bool SelfInKillLine(GameEngine2 g, int seat, PlayerState me)
        {
            foreach (var o in g.Players.Where(p => p.Alive && p.Seat != seat))
                if (KillLine(g, o, me) <= 2) return true;
            return false;
        }

        /// <summary>攻击方 atk 击杀 victim 大约还需多少次攻击：HP ÷ 每次攻击的期望命中。</summary>
        static float KillLine(GameEngine2 g, PlayerState atk, PlayerState victim)
        {
            float perAttack = 1f / Math.Max(1, victim.Hand.Count);   // 抽中英雄牌≈1点HP伤害
            return victim.HP / Math.Max(0.05f, perAttack);           // ≈ HP × 手牌数
        }

        // ------------------------------------------------------------------
        // 抉择
        // ------------------------------------------------------------------

        public bool ChooseGuardIntercept(GameEngine2 g, int seat, Card drawn, int attackerSeat)
        {
            var me = g.Players[seat];
            // 2026-10-21 用户裁定：技能点一共2个、法术5种——超过研习能力的法术在手里没有意义，
            // 侍卫/牧师身体只保"手里最后一张法术牌"（那是研习与释放的本钱）；多余的法术被抽中等于帮你腾格子。
            if (GameEngine3.IsSpellType(drawn.Type))
                return me.Hand.Count(c => GameEngine3.IsSpellType(c.Type)) <= 1;
            int style = _w.BlockStyle;
            if (style == 2 || !_mod.GuardDiscipline)
            {
                // 风格2=见刀就挡，2026-10-09 用户裁定例外：
                // 亡灵——被抽中本来自动免疫回手，挡它纯浪费侍卫；
                // 牧师——被抽中进弃牌堆触发亡语回收1张，挡它等于剥夺自己的回收。
                if (drawn.Type == CardType.Undead || drawn.Type == CardType.Priest) return false;
                return true;
            }
            bool sacHero = g.HeroOf(seat).PriestSacrifice;
            // 牺牲专属 style3：只有"挡刀 body 是牧师"时才套用 EV 账——
            // 亡语能回收的牌(弃牌堆公开最高价值) + 被挡下的牌 > 10 才挡；侍卫挡刀仍走通用纪律
            if (sacHero && g.PriestSacStyle == 3 && !me.Hand.Any(c => c.Type == CardType.Guard))
            {
                if (drawn.Type == CardType.Guard || drawn.Type == CardType.Maid) return false;
                float recover = g.Discard.Where(c => c.Type.IsRole() && c.Type != CardType.Hero)
                                         .Select(ValueOf).DefaultIfEmpty(0).Max();
                return recover + ValueOf(drawn) > 10f;
            }
            // 教学版挡刀纪律：侍卫只值高价值目标，不为低值牌浪费
            if (drawn.Type == CardType.Guard) return false;                          // 侍卫本身：挡了变1张手牌，血亏（防御性：引擎实际不把侍卫抽中送进这里）
            if (drawn.Type == CardType.Maid) return false;                           // 侍女：低值牌，硬吃
            // 2026-10-13 用户裁定修复：满血合成牌（2HP）被抽中只扣1生命还回手——挡它纯浪费侍卫。
            // 只剩1生命时这套刀会真死它，回落到下方正常价值评估（女祭司8分档才会被挡）。
            if (drawn.SynthHP >= 2) return false;
            if (drawn.Type == CardType.Hero)
            {
                // 英雄牌被抽中=直接掉1HP回手。医师满血不挡（2026-10-09 用户裁定：挨刀启动回血引擎）；
                // 保血版：英雄牌永远挡，医师满血为例外。教学版"残血才挡"对医师满血天然等价于不挡。
                if (style == 3) return !(g.HeroOf(seat).Id == 0 && me.HP >= me.MaxHP);
                return me.HP < me.MaxHP;
            }
            if (drawn.Type == CardType.Minister && g.HasArmGuard(seat)) return false; // 带刀侍卫buff下大臣故意送死触发反击（2026-10-02起布防范围还含带刀侍卫/守卫，它们低值硬吃自然进弃牌也会反击）
            // 2026-10-09 用户裁定：带刀侍卫/军机大臣按"≥8分档"挡刀——KeepValue 同为7（军机大臣是10-02用户指示8→7），
            // 但挡刀价值另算：带刀侍卫=亡语布防核心被抽走则布防体系塌一半；军机大臣=限定勒索爆发且全场仅1张。
            // 不动全局打分表（会连锁购物车EV/勒索/手牌估值），只在此提档。
            if (drawn.Type == CardType.ArmedGuard || drawn.Type == CardType.GrandMinister) return true;
            if (style == 3) return ValueOf(drawn) >= 5;   // 保血版：愿意为更多牌挡刀（阈值放宽）
            return ValueOf(drawn) >= 8;   // 军火库(9) / 双药女巫(8) / 合成牌双技女祭司(8)
        }

        /// <summary>解药/牧师亡语的回收目标（2026-10-04 测试任务2，ReviveStyle 旋钮）：
        /// 1=最高KeepValue（默认）2=优先驱魔师 3=优先警察 4=优先亡灵 5=优先侍卫 6=优先守卫 7=优先青苹果；优先目标不在场则回退最高价值。
        /// 2026-10-08 用户裁定：风格5 不全局开（会把医师 41→29、猎巫 33→25），改为牺牲英雄（HeroRoster Id=6）在默认风格1 下自动套用风格5（+14.6pp，远超噪声）；显式设置其他风格（2~7）时尊重旋钮，保证 StatSupport 可 A/B。</summary>
        public int ChooseFromDiscard(GameEngine2 g, int seat, List<Card> options)
        {
            // 3代·点燃亮牌（2026-10-21② 用户强化）：选项若是某玩家手牌（弃牌堆/牌库顶选项不会在手牌里）=点燃的看2选1——
            // 点掉价值最高的一张（军火库9/女祭司10自然成为首选），不走回收风格（牺牲英雄的"优先侍卫"不该污染点燃）。
            if (g is GameEngine3 && options.Count > 0 && g.Players.Any(p => p.Hand.Contains(options[0])))
                return options.OrderByDescending(c => KeepValue(c.Type)).First().Id;
            // （3代·炼金术"从牌库回收"版的选择分支已随 2026-10-22 中午"直接释放"重做整体移除——炼金术不再产生 Options）
            int style = _w.ReviveStyle;
            if (style == 1 && g.HeroOf(seat).Id == 6) style = 5;   // 牺牲英雄专属：优先复活侍卫（挡刀肉身回归）
            CardType? fav = null;
            switch (style)
            {
                case 2: fav = CardType.Exorcist; break;
                case 3: fav = CardType.Police; break;
                case 4: fav = CardType.Undead; break;
                case 5: fav = CardType.Guard; break;
                case 6: fav = CardType.Warden; break;
                case 7: fav = CardType.GreenApple; break;
            }
            if (fav.HasValue)
            {
                var c = options.FirstOrDefault(x => x.Type == fav.Value);
                if (c != null) return c.Id;
            }
            return options.OrderByDescending(c => KeepValue(c.Type)).First().Id;   // style1 与所有风格的回退：最高价值
        }

        public int ChooseDeathrattleTarget(GameEngine2 g, int seat, List<int> candidates)
        {
            var me = g.Players[seat];
            if (!_mod.ThreatAssess) return candidates[0];
            return candidates.OrderByDescending(s => ThreatScore(g, me, g.Players[s])).First();
        }

        // ------------------------------------------------------------------
        // 威胁评估：输出（手牌）- 斩杀线 - 集火跟车 - 记仇 - 军火库记忆
        // ------------------------------------------------------------------

        GameAction PickTarget(List<GameAction> acts, GameEngine2 g, PlayerState me)
        {
            if (!_mod.ThreatAssess) return acts[0];   // 无威胁评估：固定打座位最靠前的
            return acts.OrderByDescending(a => ThreatScore(g, me, g.Players[a.TargetSeat])).First();
        }

        /// <summary>攻击类行动的索敌：轮转基础分 + 威胁分×ThreatInAttack（威胁挂钩程度可调，0=纯轮转）。</summary>
        GameAction PickAttackTarget(List<GameAction> acts, GameEngine2 g, PlayerState me)
        {
            if (_w.PressureStyle == 1) return PickPressureTarget(acts, g, me);   // 对赌施压：双边账本索敌（2026-10-12 用户设计）
            if (!_mod.RotateFire) return acts[0];     // 无轮流火力：固定打座位最靠前的
            return acts.OrderByDescending(a => AttackScore(g, me, g.Players[a.TargetSeat])
                                              + _w.ThreatInAttack * ThreatScore(g, me, g.Players[a.TargetSeat])).First();
        }

        /// <summary>
        /// 对赌施压索敌（PressureStyle=1）。账本 gap[p] = 我打 p 的次数 − p 打我的次数（普攻/军火库/毒药均为投入，来自公开 OnHostile）。
        /// 2026-10-19 升级（用户裁定方案b）：**账本从"整体替换索敌"降级为"叠加在完整 AttackScore+威胁分 上的人格加权"**——
        /// 10-12 原版完全绕过默认索敌，而默认路径此后持续进化（合纵连横/连击graduated/斩杀窗口），原版被全面压制
        /// （配对 600 局实测 11 英雄全部负收益，均值 −8.1pp）。现在：轮转/血量/威胁精化全员参与竞争，
        /// 账本只在分差范围内说话（追平 +6~8 / 续压 +0~3+连击延续），让"账本人格"可听但不夺权。
        /// 账本语义保留：追平阶段逆差加权（两个逆差优先追差距小的）；施压阶段维持最小领先、领先达标(cap)即转火下一条账本；
        /// 1v1：唯一目标全力攻击。
        /// </summary>
        GameAction PickPressureTarget(List<GameAction> acts, GameEngine2 g, PlayerState me)
        {
            int aliveOpp = g.Players.Count(x => x.Alive && x.Seat != me.Seat);
            if (aliveOpp == 1) return acts[0];   // 1v1：唯一目标
            return acts.OrderByDescending(a => AttackScore(g, me, g.Players[a.TargetSeat])
                                               + PressureLedgerBonus(g, me, a.TargetSeat)
                                               + _w.ThreatInAttack * ThreatScore(g, me, g.Players[a.TargetSeat])).First();
        }

        /// <summary>账本人格加权（2026-10-19 升级版）：gap<0 追平（+PressureChaseBonus，逆差越小越急）；
        /// 0≤gap&lt;PressureLeadCap 续压（PressureLeadBonus 线性衰减到 0，上回合目标另加 PressureStreak 连击延续）；
        /// gap≥cap 该账本已赢下 → 0，交还给轮转/威胁（转火窗口）。</summary>
        float PressureLedgerBonus(GameEngine2 g, PlayerState me, int targetSeat)
        {
            float gap = _duelDealt[targetSeat] - _duelTaken[targetSeat];
            if (gap < 0)
                return _w.PressureChaseBonus + (_w.PressureLeadCap - Math.Min(-gap, _w.PressureLeadCap));   // +6~8（cap=2 默认）
            if (gap < _w.PressureLeadCap)
            {
                float b = _w.PressureLeadBonus * (_w.PressureLeadCap - gap) / _w.PressureLeadCap;          // +3→0 线性衰减
                if (targetSeat == _lastAttackTarget) b += _w.PressureStreak;                               // 连击延续
                return b;
            }
            return 0f;
        }

        /// <summary>
        /// 普攻独立攻击分（轮流火力，放弃绝对理性）：
        ///   1. 我越久没打的目标分越高（最近打过的尽量不打 → 全场火力轮转）；
        ///   2. 本回合没人碰过的目标加分（火力散布 → 装死不能隐身）；
        ///   3. 1v1 时唯一目标攻击分拉满。
        /// 两个例外：强攻英雄维持连击不换目标；另有血量分（优先打血量更高者，无斩杀奖励）与"被封印牧师"攻击加分（2026-10-04）。
        /// 军火库单独加分已废弃（2026-10-04）：已知军火库经情报进入威胁分的手牌价值估算，自然按 9 分计。
        /// </summary>
        float AttackScore(GameEngine2 g, PlayerState me, PlayerState p)
        {
            float s = 0;
            int last = _lastAttackedOn[p.Seat];
            if (last == me.OwnTurnCount) s -= _w.SameTurnPenalty;                       // 本回合已打过（攻城锤额外普攻兜底）
            else if (last == me.OwnTurnCount - 1) s -= _w.JustHitPenalty;              // 上一回合刚打过
            else
            {
                int stale = last < 0 ? 3 : Math.Min(3, me.OwnTurnCount - last - 1);    // 空闲越久分越高，封顶3档
                s += stale * _w.StalenessBonus;
            }
            if ((_recentAttacksOn?[p.Seat] ?? 0) == 0) s += _w.HitThisRoundBonus;      // 规则2：本回合没人碰过 → 加分
            if (g.Players.Count(x => x.Alive && x.Seat != me.Seat) == 1) s += _w.DuelAttackBonus;  // 规则3：1v1拉满
            if (p.HP <= 1) s += _w.LastHitBonus;                                       // 斩杀优先（默认0=关闭）
            // 致命一击联动（2026-10-19 被动版）：目标HP=1且手牌（不含英雄牌）≤5 → 我的攻击必中其英雄=锁定淘汰
            //（跳过抽牌/挡刀）。淘汰赛里淘汰一人≈保底第二+对手遗物归我，价值按淘汰档计而非一牌档。
            if (me.HeroId == 10 && p.HP == 1 && p.Hand.Count(x => x.Type != CardType.Hero) <= 5) s += _w.DeathblowGuaranteedBonus;
            if (_w.NextSeatBonus != 0 && p.Seat == (me.Seat + 1) % g.Players.Count) s += _w.NextSeatBonus;   // 痛击下家（2026-10-08 用户理论实验，默认0）
            {   // 开局痛击下家（仅前2自己回合）：旋钮 + 攻城锤专属+2（2026-10-09 用户裁定保留——配对 Δ+0.008/+2.0pp 未超噪声但为正收益，且与其开局双爆窗口主题契合）
                float earlyNext = _w.NextSeatEarlyBonus + (me.HeroId == 3 ? 2f : 0f);
                if (earlyNext != 0 && me.OwnTurnCount <= 2 && p.Seat == (me.Seat + 1) % g.Players.Count) s += earlyNext;
            }
            // 合纵连横：自认不是最强 → HP分取正（弱者联合压强者）；自认最强 → 取负（各个击破先斩弱者）
            float hpBonus = IsStrongest(g, me) ? _w.HpAttackBonusLeader : _w.HpAttackBonus;
            s += p.HP * hpBonus;
            if (g.HasSealed(p.Seat, CardType.Priest)) s += _w.SealedPriestAttackBonus; // 被封印的牧师不能挡刀/亡语，打他最划算
            // 强攻graduated连击（2026-10-09 用户设计）：第2发+2 / 第3发（触发发）+10；触发后清零=转火窗口。
            // 用引擎公开计数（FocusStreakTarget/Count），毒药/军火库/免费攻击都算进连击，与引擎判定天然一致。
            if (me.HeroId == 4 && p.Seat == g.FocusStreakTarget(me.Seat))
            {
                int n = g.FocusStreakCount(me.Seat);
                if (n == 1) s += _w.FocusSecondBonus;
                else if (n == 2) s += _w.FocusThirdBonus;
            }
            s += (float)_rng.NextDouble() * 1.0f;
            return s;
        }

        /// <summary>对手手牌价值估算（2026-10-04 用户设计，取代军火库单独针对）：
        /// 被验出的牌按 KeepValue 逐张计（驱魔抽出/处决、警察扣押命中、亡灵挡刀、解药/牧师亡语回收，全部公开播报），
        /// 其余未知牌按全库均值计。2026-10-15 守卫改版：被护住（无法选取）的牌不会被攻击抽到，有效厚度要扣掉公开的张数。
        /// 一张已知的军火库自然值 9 分，不再需要单独加分。</summary>
        float HandValueEst(GameEngine2 g, PlayerState p)
        {
            int total = p.Hand.Count - g.GuardProtectedCount(p.Seat);
            float known = 0;
            int knownN = 0;
            if (_intel.TryGetValue(p.Seat, out var info))
                foreach (var kv in info) { known += KeepValue(kv.Key) * kv.Value; knownN += kv.Value; }
            knownN = Math.Min(knownN, total);
            return known + (total - knownN) * DeckMean;
        }

        float ThreatScore(GameEngine2 g, PlayerState me, PlayerState p)
        {
            float s;
            if (_mod.Desperate && (me.Hand.Count <= 2 || me.HP <= 1))
            {
                // 绝望阎王：自己快死了，谁跟我结仇我打谁，输赢另说（2026-10-15：扣掉被守卫护住的有效厚度）
                s = (p.Hand.Count - g.GuardProtectedCount(p.Seat)) * 0.5f + _grudge[p.Seat] * 3f + (p.MaxHP - p.HP) * 1f;
            }
            else
            {
                s = HandValueEst(g, p) * _w.HandSizeThreat;           // 对方手牌价值估算（已知牌按真实分、未知按均值，扣被护住的张数）
                s += (p.MaxHP - p.HP) * _w.LowHpBonus;                // 低血量：接近斩杀线，优先补刀
                s += p.HP * _w.HpThreat;                              // 血量入威胁：满血强者=头号公敌（2026-10-04 旋钮，默认0）
                if (_w.KillLineBonus != 0) s -= KillLine(g, me, p) * _w.KillLineBonus * 0.1f;    // 击杀所需攻击次数越少越优先
                if (IsStrongest(g, me) && p.HP <= 1) s += 8f;         // 斩杀需自认最强
                if (_mod.Grudge) s += _grudge[p.Seat] * _w.Grudge;
            }
            s += (float)_rng.NextDouble() * 1.5f;
            return s;
        }

        /// <summary>驱魔索敌：威胁 + (目标公开亡灵次数 - 弃牌堆已阵亡亡灵)×权重。公开信息，不作弊。
        /// 猎巫行动（HeroId 5）：每个"验出女巫"情报额外加 WitchHuntIntelBonus——确定处决，按女巫价值计。</summary>
        float ExorciseScore(GameEngine2 g, PlayerState me, PlayerState p)
        {
            int deadUndead = g.Discard.Count(c => c.Type == CardType.Undead);
            float s = ThreatScore(g, me, p) + _w.ExorciseUndeadBonus * (_undeadSeen[p.Seat] - deadUndead);
            if (me.HeroId == 5 && _intel.TryGetValue(p.Seat, out var wi) && wi.TryGetValue(CardType.Witch, out int wn))
                s += _w.WitchHuntIntelBonus * wn;
            return s;
        }

        /// <summary>贿赂 EV（2026-10-14③ 用户重设计，④加总系数）：无威胁项、无前置条件。
        /// EV = 给出后剩余手牌（非英雄）的价值均值 × BribeEvCoeff——贿赂=自选哪张牌"被打"，丢垃圾抬均值、给好牌=资助敌人均值自降；
        /// 安放完炸弹的恐怖分子按 0 分计（爆炸以安放者敌我划线、敌人最终什么都得不到）。
        /// 军机大臣"先给出去反手勒索回来"的天然 combo 不入公式（发生即大赚，由局势自然产生）。
        /// ④：系数必需——纯均值与普攻EV（≈全库均值4.1）同档，实测每局530次贿赂、全场停战对局瘫痪（20000步打不完）、
        /// 美人计狂用侍女贿赂的局胜率反而 25.7% vs 不用 37.5%（停战=谁也杀不掉谁）。0.5=停战+自选牌≈半张牌的价值。
        /// （2026-10-19 最终版）：美人计改为被动——侍女贿赂的加成不再是"结盟2回合"，而是"魅惑=攻击重定向到目标走完整伤害判定"
        /// 的价值（CharmRedirectBonus，默认+3≈转嫁一次攻击的期望）。
        /// ⑥（2026-10-14 晚用户裁定）：典狱长在押人质给出价=0（人质强制归还原拥有者，给了等于没给，且我方手牌均值里人质也不按面值计——
        /// 否则"留人质给真牌"会被公式错误奖励）；对手是强攻且连击正好打到我第2发（下一发=第3发=触发追加攻击）时 ×2——
        /// 买停战=断连击（引擎结盟时清零其连击）+省2点伤害，比一回合免打值钱一倍。
        /// ⑧（2026-10-19 用户实战裁定，两条硬门槛——"无前置条件"原则让位于实战回放）：给出后剩余牌 < BribeMinRemaining（默认3）不贿赂；
        /// 给出后剩余均值 > BribeMaxAvg（默认5）不贿赂。理由：手牌少=肉身薄送不起；质量高=没垃圾可丢，给真牌=资助敌人还自损。</summary>
        float BribeScore(GameEngine2 g, PlayerState me, PlayerState target, Card give)
        {
            float ValueOf(Card c)
            {
                if ((c.Type == CardType.Terrorist && c.BombActive) || g.IsHostage(c)) return 0f;
                // 2026-10-21 用户裁定：手里法术≤2张时是研习材料不是垃圾（2技能点要吃2张牌本体）——
                // 给出代价按材料价4计，AI贿赂时会优先留下法术；≥3张充足时法术就是1分 fodder（用户确认）。
                if (g is GameEngine3 && GameEngine3.IsSpellType(c.Type)
                    && me.Hand.Count(x => GameEngine3.IsSpellType(x.Type)) <= 2)
                    return 4f;
                return (float)KeepValue(c.Type);
            }
            var remaining = me.Hand.Where(c => c.Type != CardType.Hero && c.Id != give.Id).ToList();
            if (remaining.Count < _w.BribeMinRemaining) return 0f;   // ⑧ 手牌薄不贿赂
            float avg = remaining.Select(ValueOf).DefaultIfEmpty(0f).Average();
            if (avg > _w.BribeMaxAvg) return 0f;                   // ⑧ 手牌质量好不贿赂
            if (g.HeroOf(target.Seat).FocusFire && g.FocusStreakTarget(target.Seat) == me.Seat && g.FocusStreakCount(target.Seat) >= 2)
                avg *= 2f;   // ⑥ 贿赂断强攻第3发
            float charmBonus = 0f;
            if (me.HeroId == 7 && give.Type == CardType.Maid)
                charmBonus = _w.CharmRedirectBonus;   // 2026-10-19 最终版：侍女贿赂触发魅惑（攻击重定向到目标走完整伤害判定）
            // （2026-10-19 晚"组件留牌+调度检索+目标威胁加权"实验已按用户裁定整体回滚——囤组件成本>边际魅惑价值，详见规则文档）
            return avg * _w.BribeEvCoeff + charmBonus;
        }

        static bool IsStrongest(GameEngine2 g, PlayerState me)
        {
            int maxOpp = g.Players.Where(p => p.Alive && p.Seat != me.Seat)
                                  .Select(p => p.Hand.Count).DefaultIfEmpty(0).Max();
            return me.Hand.Count >= maxOpp;
        }

        /// <summary>牌的预期收益档位（喂牌/藏牌/勒索决策用，数值=价值档，非线性）。2026-10-02 按用户要求重调：大臣下调、侍卫/牧师上调、驱魔/警察/守卫/女巫下调。</summary>
        public static int KeepValue(CardType t)
        {
            switch (t)
            {
                case CardType.Arsenal: return 9;        // 每回合1牌收益，持续型强牌
                case CardType.Witch: return 8;          // 双药≈2牌收益，一次性强神牌
                case CardType.GrandMinister: return 7;  // 勒索爆发（限定技一次性，600局实测持有者胜率低于人均）
                case CardType.ArmedGuard: return 7;     // 亡语布防（2026-10-02 起布防范围含侍卫/大臣/带刀侍卫/守卫）
                case CardType.Priest: return 6;         // 亡语回收≈1牌，且回收池会随对局变肥
                case CardType.Warden: return 5;         // 藏牌防御
                case CardType.GreenApple: return 5;     // 受伤后2回合回1HP + 被抽中变被抢（对方+1牌我方-1牌，净差2张）
                case CardType.Exorcist: return 4;       // 处决亡灵/女巫=1牌，否则仅情报
                case CardType.Police: return 4;         // 封印关键牌=限制对手，不确定收益
                case CardType.Undead: return 4;         // 每被抽中1次≈少受1伤害，持续收益
                case CardType.Guard: return 3;          // 挡刀≈省1牌（2026-10-02 上调：被低估）
                case CardType.Terrorist: return 3;      // 安放炸弹（2026-10-13 改版）：本体是消耗品（爆炸自灭），价值在技能——给3档肉身价
                case CardType.Priestess: return 10;     // 合成牌：双限定技（复活任意角色 + 延迟回血）。2026-10-13 用户调价 8→10：配方的女巫按用药后动态价(8/5/2)计后，药尽女巫能把 EV 拉正
                case CardType.Paladin: return 9;        // 合成牌：2点生命挡刀身体 + 耗尽时牧师亡语。2026-10-13 用户调价 6→9（"至少9分"：挡刀身体+亡语+可复活套利）
                case CardType.Valkyrie: return 6;       // 合成牌：2点生命挡刀身体。2026-10-13 用户调价 5→6
                case CardType.Jailer: return 10;        // 合成牌：扣押1张人质+封印（2026-10-14 削弱：2回合冷却+人质扣2回合，原是每回合1张/扣1回合）。2026-10-13 用户调价 5→7→10：600局实测合成者 1.324 分/46.2% 胜全场最强，超标上调；削弱后价值待重测
                case CardType.Inquisitor: return 5;     // 合成牌：每回合审判（处决异端）。2026-10-13 用户调价 5→7→5：基础档降回5，强度放进猜测法——外面每个女巫+1分、每个亡灵+1分（2HP身体更能扛线完成捉鬼，用户明确加强点）；猎巫行动英雄不合成（自带处决）
                case CardType.Minister: return 1;       // 结盟=防御资源（2026-10-02 下调：4张的地板牌）
                case CardType.Maid: return 1;
                case CardType.Worker: return 4;         // 行动点+1（2026-10-13 回归牌库）=【首张】工人价：MP+1 持续收益+加厚手牌。
                                                       // 不叠加→第二张起只值肉身价3（OwnCardValue 处理）；用户实测直觉"工人挺强"，4>旧3档保守价；DeckMean 自动跟动。
                default: return 1;
            }
        }

        /// <summary>全库平均价值（47 张牌库构成 × KeepValue 加权，2026-10-13 加入工人×3）：普攻 EV 的计量单位。
        /// 2026-10-02 起购物车 EV 统一用"价值档"作单位，普攻 = 均值×AttackEvCoeff（1=一张随机牌的期望价值）。
        /// 调整打分表后此值自动重算——改 KeepValue 不需要改这里。</summary>
        static readonly float DeckMean = ComputeDeckMean();

        static float ComputeDeckMean()
        {
            var counts = new Dictionary<CardType, int>
            {
                { CardType.Worker, 3 },
                { CardType.Undead, 4 }, { CardType.Exorcist, 4 }, { CardType.Priest, 4 },
                { CardType.Police, 4 }, { CardType.Guard, 4 }, { CardType.Witch, 3 },
                { CardType.Arsenal, 1 }, { CardType.ArmedGuard, 1 }, { CardType.Minister, 4 },
                { CardType.GrandMinister, 1 }, { CardType.Warden, 3 }, { CardType.Maid, 4 },
                { CardType.GreenApple, 3 }, { CardType.Terrorist, 4 }
            };
            int n = 0, v = 0;
            foreach (var kv in counts) { n += kv.Value; v += kv.Value * KeepValue(kv.Key); }
            return v / (float)n;
        }

        static bool IsSkillCard(CardType t)
        {
            return t == CardType.Exorcist || t == CardType.Police || t == CardType.Witch
                || t == CardType.Arsenal || t == CardType.Minister || t == CardType.GrandMinister
                || t == CardType.ArmedGuard || t == CardType.Warden;
        }
    }
}
