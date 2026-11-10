using System.Collections.Generic;

namespace SandKill.Core
{
    /// <summary>技能性质（2026-10-29 用户数据模型，ai优化思路.docx）：主动 / 被动（亡语、光环、弹窗触发）/ 无。</summary>
    public enum AbilityKind { None, Active, Passive }

    /// <summary>主动技刷新档（用户数据模型）：PerTurn=每回合刷新 / Per2Turns=2回合冷却 / Null=不刷新（一次性或持续生效）。
    /// 所有主动技蓝耗都是1，cost 不构成区分维度，冷却档才是主动技的本质区别。</summary>
    public enum CooldownTier { None, PerTurn, Per2Turns, Null }

    /// <summary>
    /// 一张牌的元数据：它"是什么"的描述性数据。
    ///
    /// 为什么需要这张表（原来有什么问题）：
    ///   一张牌的名字散在 CardTypeExtensions.Name() 的 switch 里、蓝耗散在引擎的 const 区、
    ///   界面说明散在 GameUI.PassiveDesc() 里——改一张牌要改好几处，还会漂移。
    ///   现在"身份数据"集中在这唯一一份，引擎和 UI 都来查表。
    ///
    /// 2026-10-29 用户数据模型补全（ai优化思路.docx）：ability 二分 passive/主动，主动按 cooldown(1/2/null) 区分；
    /// 彗星=passive（反制弹窗），其余法术=主动-null（每张1次+研习解锁前置）；附 BodyHp 与成长分配置。
    ///
    /// 边界：
    ///   这里只管**描述性数据**（名字、蓝耗、说明文字、属性分类、价值配置）。
    ///   技能**怎么结算**（抽几张、扣几点HP、沉默几回合）仍然是引擎的职责，不进表。
    /// </summary>
    public class CardDef
    {
        public CardType Type;
        public string Name;           // 中文名
        public bool IsRole;           // 是否角色牌（非工人）
        public int SkillCost;         // 主动技蓝耗（2代规则；0 = 没有主动技）。基础攻击永远免费，不在此列。
        public int SubSkillCost;      // 第二主动技蓝耗（-1 = 没有第二技能）。目前只有女巫：毒药1蓝 / 解药1蓝。
        public string SkillTag;       // 手牌副标签里的动作短语（"可扣押"）——被动牌留空
        public string PassiveTag;     // 被动牌的手牌标签（"免疫攻击"）——主动牌留空
        public string SkillDesc;      // 主动技完整说明（玩家点开牌时看）
        public string PassiveDesc;    // 被动完整说明
        public string Desc2;          // 2代口径说明（1/2代语义不同的牌用；空=回退 SkillDesc/PassiveDesc）。如工人：1代=生存之本，2代=行动点+1

        // ---- 2026-10-29 用户数据模型：属性分类 ----
        public AbilityKind Ability = AbilityKind.None;    // 技能性质：主动 / 被动(亡语·光环·弹窗) / 无
        public CooldownTier Cooldown = CooldownTier.None; // 主动技刷新档（1回合 / 2回合 / null=不刷新）
        public int BodyHp = 1;                            // 肉身HP：普通牌1 / 合成牌2 / 英雄3 / 法术0（描述性；引擎结算仍归引擎）

        // ---- 2026-10-29 成长分配置（中级AI动态估值；用户：先只挂亡灵/牧师/警察，不贪多）----
        // 成长轴=全场手牌萎缩度（公开信息：平均非英雄手牌从10张萎缩到0）。前期取 GrowthBase，残局取 GrowthCap。
        public int GrowthBase = -1;   // 成长价值下限（-1 = 无成长分）
        public int GrowthCap = -1;    // 成长价值上限（-1 = 无成长分）

        public bool IsSpell { get { return Type >= CardType.Alchemy; } }                              // 法术牌（3代区段）
        public bool IsSynth { get { return Type >= CardType.Priestess && Type <= CardType.Inquisitor; } } // 合成牌区段
        public bool HasGrowth { get { return GrowthCap >= 0; } }

        // ---- 2026-10-30 属性抽象（用户：改变思考对象——AI 面向手牌级聚合属性而非单张牌）----
        public int LimitUnits = 0;        // 限定技单位贡献：军机大臣=1、恐怖分子每张=1。女巫=动态（双药2/单药1/药尽0，按牌状态在聚合函数算）；
                                          // 大臣=手牌级规则（没法随用，不管几张整场算1，聚合函数算）——这两类元数据保持0，避免双重计数
        public bool IsDefendBody = false; // 挡刀/防御身体（引擎口径 gen3/GameEngine2.cs 挡刀判定）：侍卫/女武神/圣骑士人人可用；
                                          // 牺牲英雄的牧师为英雄条件（聚合函数 priestCanBlock 参数另计），不记入基础抽象

        /// <summary>玩家点开牌时看的说明：2代口径优先，未单独写就回退通用说明。</summary>
        public string DisplayDesc { get { return !string.IsNullOrEmpty(Desc2) ? Desc2 : (SkillDesc.Length > 0 ? SkillDesc : PassiveDesc); } }

        /// <summary>有没有主动技（决定副标签显示"X蓝·动作"还是被动标签）。</summary>
        public bool HasActiveSkill { get { return SkillCost > 0; } }
    }

    /// <summary>卡牌元数据表：每种 CardType 一份定义。加新牌 = 在这里加一行。</summary>
    public static class CardDefs
    {
        public static readonly Dictionary<CardType, CardDef> All = new Dictionary<CardType, CardDef>
        {
            { CardType.Worker, new CardDef {
                Type = CardType.Worker, Name = "工人", IsRole = false,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "存活之本",
                SkillDesc = "", PassiveDesc = "维持你的存活，失去所有工人就输了。",
                Desc2 = "被动：手牌中有【工人】，行动点上限+1（多张不叠加）。（2026-10-13 起工人回归2代牌库）",
                Ability = AbilityKind.Passive, Cooldown = CooldownTier.Null } },   // 持续光环

            { CardType.Undead, new CardDef {
                Type = CardType.Undead, Name = "亡灵", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "免疫攻击",
                SkillDesc = "", PassiveDesc = "被动：免疫攻击，被攻击抽中时自动返回手牌。驱魔师可将其处决。",
                Ability = AbilityKind.Passive, Cooldown = CooldownTier.Null,
                GrowthBase = 3, GrowthCap = 5 } },   // 2026-10-29 用户：前期价值低后期高——手牌越薄每刀越容易抽到它，免疫越值钱

            { CardType.Exorcist, new CardDef {
                Type = CardType.Exorcist, Name = "驱魔师", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可捉亡灵", PassiveTag = "",
                SkillDesc = "主动：抽对手一张牌验明正身，是亡灵则当场处决，否则归还。（驱魔方会获知抽到的牌面）",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.PerTurn,
                GrowthBase = 2, GrowthCap = 5 } },   // 2026-10-29④ 用户：挂主动技名额阶梯（同警察轴，base=4原值）：剩1张=5（升值）/2张=4/3张=3（贬值）/>3张=2（有工人=3）——敌人手中亡灵数成长漏风，换确定性数据

            { CardType.Police, new CardDef {
                Type = CardType.Police, Name = "警察", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可沉默", PassiveTag = "",
                SkillDesc = "主动：封住对手一张手牌（随机选择非英雄牌），命中的牌型公开播报：目标的下个回合，该牌无法使用技能、被动也失效。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.PerTurn,
                GrowthBase = 3, GrowthCap = 5 } },   // 2026-10-29④ 用户：主动技名额阶梯（base=4=KeepValue原值）：剩1张=5（升值）/2张=4/3张=3（贬值）/>3张=2（有工人=3；无工人档为推测值待确认）——对手手牌质量成长漏风（2个敌人不知按谁），改用确定性数据

            { CardType.Priest, new CardDef {
                Type = CardType.Priest, Name = "牧师", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "亡语:回收",
                SkillDesc = "",
                PassiveDesc = "被动：被抽走进入弃牌堆时触发亡语，从弃牌堆回收一张角色牌（不能回收牧师和英雄）。",
                Ability = AbilityKind.Passive, Cooldown = CooldownTier.Null,
                GrowthBase = 1, GrowthCap = 7 } },   // 2026-10-29③ 用户重裁：轴=弃牌堆牌数（弃牌堆0张→1分，每多3张+1分，上限7）；弃牌堆质量不好判定，按用户原则改用确定性的数量（牧师亡语回收的质量随弃牌堆变肥而上升）

            { CardType.Guard, new CardDef {
                Type = CardType.Guard, Name = "侍卫", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "可挡一刀",
                SkillDesc = "",
                PassiveDesc = "被动：被攻击时可选择挡刀，代替被抽中的牌进入弃牌堆。",
                Ability = AbilityKind.Passive, Cooldown = CooldownTier.Null,
                GrowthBase = 1, GrowthCap = 4,
                IsDefendBody = true } },   // 2026-10-29④ 用户：挂主动技名额阶梯（base=3=KeepValue原值）：剩1张=4（升值）/2张=3/3张=2（贬值）/>3张=1（有工人=2）——手牌有无值得守护单位成长漏风，换确定性数据；弹窗触发；防御身体（2026-10-30 属性抽象）

            { CardType.Witch, new CardDef {
                Type = CardType.Witch, Name = "女巫", IsRole = true,
                SkillCost = 1, SubSkillCost = 1, SkillTag = "", PassiveTag = "",
                SkillDesc = "主动：毒药=额外攻击一次（1蓝）；解药=救活上一回合内阵亡的角色牌（1蓝）。各限一次。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.Null } },   // 双限定技=不刷新

            { CardType.Arsenal, new CardDef {
                Type = CardType.Arsenal, Name = "军火库", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可追加攻击", PassiveTag = "",
                SkillDesc = "主动：每回合可额外攻击一次。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.PerTurn } },

            { CardType.Hero, new CardDef {
                Type = CardType.Hero, Name = "英雄", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "永不离手",
                SkillDesc = "", PassiveDesc = "英雄牌永不离开手牌，被攻击/扣押/勒索打中时改为扣1点HP。",
                Ability = AbilityKind.Passive, Cooldown = CooldownTier.Null, BodyHp = 3 } },

            { CardType.ArmedGuard, new CardDef {
                Type = CardType.ArmedGuard, Name = "带刀侍卫", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "亡语反击光环",
                SkillDesc = "",
                PassiveDesc = "光环：只要带刀侍卫在你手牌中，你的侍卫、大臣、带刀侍卫和守卫常驻亡语“被丢弃时立即攻击1次”（无需发动、不耗蓝）。被沉默整套时失效；被典狱长扣押离手即失效。",
                Ability = AbilityKind.Passive, Cooldown = CooldownTier.Null,
                GrowthBase = 1, GrowthCap = 8 } },   // 2026-10-29④ 用户：光环对象在手加分结构（非阶梯）——本体基础1分+手牌有侍卫+3（可主动触发挡刀）+有大臣+2+有守卫+2；独自在手≈1，配齐≈8

            { CardType.Minister, new CardDef {
                Type = CardType.Minister, Name = "大臣", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "贿赂结盟", PassiveTag = "",
                SkillDesc = "主动：交一张牌给一名玩家，对方到你下回合开始前不能攻击你。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.PerTurn } },   // 每回合限1次

            { CardType.GrandMinister, new CardDef {
                Type = CardType.GrandMinister, Name = "军机大臣", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "勒索牌型", PassiveTag = "",
                SkillDesc = "主动（限定技，整局一次）：获得其他所有人手牌中某种类型的全部牌。指定【英雄】= 所有对手各扣1点HP。（1蓝）",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.Null,
                LimitUnits = 1 } },   // 限定技；限定技单位=1（2026-10-30 属性抽象）

            { CardType.Warden, new CardDef {
                Type = CardType.Warden, Name = "守卫", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "护一张牌", PassiveTag = "",
                SkillDesc = "主动：给一张手牌施加【无法选取】直到你的下回合开始。期间该牌留在手牌，但不参与任何针对手牌的判定（攻击抽牌/挡刀/驱魔/审判/扣押/人质/勒索/贿赂/合成材料）。不能选英雄牌。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.PerTurn } },

            { CardType.Maid, new CardDef {
                Type = CardType.Maid, Name = "侍女", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "免疫军火库",
                SkillDesc = "",
                PassiveDesc = "被动：免疫军火库的追加攻击（普攻和毒药照常吃）。",
                Ability = AbilityKind.Passive, Cooldown = CooldownTier.Null } },   // 免疫光环

            { CardType.GreenApple, new CardDef {
                Type = CardType.GreenApple, Name = "青苹果", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "受伤倒数",
                SkillDesc = "",
                PassiveDesc = "被动：你受伤后开始2回合倒数（再次受伤不会打断），倒数结束自动吃掉它，回复1点HP。它被攻击抽中时不会进入弃牌堆，而是被攻击者抢走。",
                Ability = AbilityKind.Passive, Cooldown = CooldownTier.Null } },   // 条件触发（受伤）

            { CardType.Terrorist, new CardDef {
                Type = CardType.Terrorist, Name = "恐怖分子", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "安放炸弹", PassiveTag = "",
                SkillDesc = "主动（1蓝，限定技——每张恐怖分子整局限安放1次，拆弹不返还）：安放一枚炸弹，2回合后爆炸——对炸弹现任持有者的所有敌方英雄各造成1点直接伤害（无视挡刀/2生命身体/亡灵免疫；护盾可吸收，不记仇），恐怖分子自己同归于尽。炸弹跟着牌走：安放后被大臣贿赂送人，爆炸以新持有者的敌我划线（可能炸回安放者自己，不能先挂弹再送人）。倒数期间被警察/police station沉默＝炸弹被直接拆除（蓝与限定次数均不返还）；恐怖分子阵亡则炸弹失效。（2026-11-09 用户改版：由无来源随机攻击改为直击英雄直伤+限定技+跟人走）",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.Null,
                LimitUnits = 1 } },   // 安放=消耗自身的一次性；限定技单位=1（2026-10-30 属性抽象）

            { CardType.Priestess, new CardDef {
                Type = CardType.Priestess, Name = "女祭司", IsRole = true,
                SkillCost = 1, SubSkillCost = 1, SkillTag = "复活/祈祷", PassiveTag = "",
                SkillDesc = "主动（双限定技，各限1次）：【复活】1行动点，从弃牌堆复活1张角色牌；【祈祷】1行动点，2回合后将自身1点生命转化为英雄的1点生命（自身共2点生命，耗尽则女祭司消失）。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.Null, BodyHp = 2 } },   // 双限定技

            { CardType.Valkyrie, new CardDef {
                Type = CardType.Valkyrie, Name = "女武神", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "挡刀·2生命",
                SkillDesc = "",
                PassiveDesc = "被动：2点生命——挡刀或被攻击直接抽中均消耗1点生命，还活着就回到手牌；生命耗尽才进入弃牌堆。",
                Ability = AbilityKind.Passive, Cooldown = CooldownTier.Null, BodyHp = 2,
                IsDefendBody = true } },   // 挡刀身体（弹窗）；防御身体（2026-10-30 属性抽象）

            { CardType.Paladin, new CardDef {
                Type = CardType.Paladin, Name = "圣骑士", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "挡刀·亡语",
                SkillDesc = "",
                PassiveDesc = "被动：2点生命——挡刀或被攻击直接抽中均消耗1点生命，生命耗尽进入弃牌堆时触发【牧师】同款亡语（被攻击抽走也算）：从弃牌堆回收一张角色牌（不能回收牧师、圣骑士和英雄）。",
                Ability = AbilityKind.Passive, Cooldown = CooldownTier.Null, BodyHp = 2,
                IsDefendBody = true } },   // 挡刀身体+亡语；防御身体（2026-10-30 属性抽象）

            { CardType.Jailer, new CardDef {
                Type = CardType.Jailer, Name = "典狱长", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "扣押", PassiveTag = "",
                SkillDesc = "主动（2回合冷却）：1行动点，从对方手牌中扣押1张牌到你手中作为人质并封印（技能+被动失效）；人质扣2回合——原拥有者的第2个回合结束归还并解除封印。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.Per2Turns, BodyHp = 2 } },

            { CardType.Inquisitor, new CardDef {
                Type = CardType.Inquisitor, Name = "审判官", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "审判", PassiveTag = "",
                SkillDesc = "主动：1行动点，随机揭示对手1张手牌；若是【亡灵】或【女巫】（异端），将其当场处决进入弃牌堆。揭示的牌型公开播报。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.PerTurn, BodyHp = 2 } },

            // ---- 法术牌（3代·法术迸发，2026-10-20 用户设计）：释放后直接消耗（进弃牌堆）；
            //      未学会前是纯白板；升级得1技能点，learn 消耗1行动点+1回合学会一种法术 ----
            // 2026-10-29 用户数据模型：彗星=passive（反制弹窗），其余法术=主动-null（每张只能用1次，
            //   与2代 null 技的区别是多一层"研习解锁"前置；消耗形态是直接消失而非变换状态）。
            { CardType.Alchemy, new CardDef {
                Type = CardType.Alchemy, Name = "炼金术", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "【2026-10-23 退环境】法术（1行动点）：从牌库回收1张你已研习的法术牌加入手牌（不含炼金术自身）。本牌已退出3代牌库，定义保留。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.Null, BodyHp = 0 } },

            { CardType.Clarity, new CardDef {
                Type = CardType.Clarity, Name = "清晰术", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "【2026-10-23 退环境】法术（1行动点）：查看牌库顶3张牌选1张入手。本牌已退出3代牌库，定义保留（其『标记』机制由点燃继承）。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.Null, BodyHp = 0 } },

            { CardType.Ignite, new CardDef {
                Type = CardType.Ignite, Name = "点燃", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "法术（1行动点，2026-10-23 重做）：查看目标对手2张手牌（不足全亮），选择其中1张点燃——挂上2回合标记；2回合后对这张牌造成1点伤害。若选中的是【军火库】或英雄牌：直接造成1点伤害，不产生标记。释放后法术牌消耗掉。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.Null, BodyHp = 0 } },

            { CardType.Thunder, new CardDef {
                Type = CardType.Thunder, Name = "雷霆", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "法术（1行动点）：对选中的玩家连续攻击，攻击次数=其当前手牌数的25%（四舍五入），逐次随机抽牌正常结算。释放后法术牌消耗掉。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.Null, BodyHp = 0 } },

            { CardType.Comet, new CardDef {
                Type = CardType.Comet, Name = "彗星", IsRole = false,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "可反制", PassiveTag = "法术",
                SkillDesc = "法术（不耗行动点）：当有人释放法术时，你可以消耗1张【彗星】抵消那个法术（被抵消的法术直接消耗、效果不结算）。需先研习（learn）获得反制权限。",
                PassiveDesc = "被动（反制）：有人释放法术时，可消耗本牌抵消之，不耗行动点。未研习前不可反制。",
                Ability = AbilityKind.Passive, Cooldown = CooldownTier.Null, BodyHp = 0 } },   // 弹窗触发（反制窗口）

            { CardType.Barrier, new CardDef {
                Type = CardType.Barrier, Name = "风墙", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "法术（1行动点，2026-10-23 由『屏障』更名重做）：对一名玩家释放，获得持续2回合的2点定向护盾——只吸收来自指定玩家的伤害（普攻/军火库/毒药/强攻追加/勒索直伤均可吸收，其余来源照常）。释放后法术牌消耗掉。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.Null, BodyHp = 0 } },

            { CardType.Glacier, new CardDef {
                Type = CardType.Glacier, Name = "冰川", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "法术（1行动点，2026-10-23 新增）：目标下2个回合合并为1个——回合照常轮到它（抽牌/倒数/联盟扣减照常），但行动点不回满、所有每回合限1次的行动（普攻/警察/驱魔等）真正不刷新；它可自主分配这两回合的行动点；不能对已被冰川冻结的目标叠加。释放后法术牌消耗掉。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.Null, BodyHp = 0 } },

            { CardType.Heal, new CardDef {
                Type = CardType.Heal, Name = "治疗术", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "法术（1行动点，2026-10-23 新增）：立刻回复英雄1点HP（满血无效果）。瞬发，替代青苹果生态位、与等级系统兼容。释放后法术牌消耗掉。",
                PassiveDesc = "",
                Ability = AbilityKind.Active, Cooldown = CooldownTier.Null, BodyHp = 0 } },
        };

        /// <summary>查表：这种牌的元数据。新牌没登记时抛异常——宁可立刻报错，不要静默显示"?"。</summary>
        public static CardDef Of(CardType t)
        {
            CardDef def;
            if (All.TryGetValue(t, out def)) return def;
            throw new System.ArgumentException("CardDefs 表里没有登记这种牌: " + t);
        }
    }
}
