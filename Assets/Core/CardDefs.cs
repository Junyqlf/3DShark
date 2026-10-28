using System.Collections.Generic;

namespace SandKill.Core
{
    /// <summary>
    /// 一张牌的元数据：它"是什么"的描述性数据。
    ///
    /// 为什么需要这张表（原来有什么问题）：
    ///   一张牌的名字散在 CardTypeExtensions.Name() 的 switch 里、蓝耗散在引擎的 const 区、
    ///   界面说明散在 GameUI.PassiveDesc() 里——改一张牌要改好几处，还会漂移。
    ///   现在"身份数据"集中在这唯一一份，引擎和 UI 都来查表。
    ///
    /// 边界：
    ///   这里只管**描述性数据**（名字、蓝耗、说明文字）。
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
                Desc2 = "被动：手牌中有【工人】，行动点上限+1（多张不叠加）。（2026-10-13 起工人回归2代牌库）" } },

            { CardType.Undead, new CardDef {
                Type = CardType.Undead, Name = "亡灵", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "免疫攻击",
                SkillDesc = "", PassiveDesc = "被动：免疫攻击，被攻击抽中时自动返回手牌。驱魔师可将其处决。" } },

            { CardType.Exorcist, new CardDef {
                Type = CardType.Exorcist, Name = "驱魔师", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可捉亡灵", PassiveTag = "",
                SkillDesc = "主动：抽对手一张牌验明正身，是亡灵则当场处决，否则归还。（驱魔方会获知抽到的牌面）",
                PassiveDesc = "" } },

            { CardType.Police, new CardDef {
                Type = CardType.Police, Name = "警察", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可沉默", PassiveTag = "",
                SkillDesc = "主动：封住对手一张手牌（随机选择非英雄牌），命中的牌型公开播报：目标的下个回合，该牌无法使用技能、被动也失效。",
                PassiveDesc = "" } },

            { CardType.Priest, new CardDef {
                Type = CardType.Priest, Name = "牧师", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "亡语:回收",
                SkillDesc = "",
                PassiveDesc = "被动：被抽走进入弃牌堆时触发亡语，从弃牌堆回收一张角色牌（不能回收牧师和英雄）。" } },

            { CardType.Guard, new CardDef {
                Type = CardType.Guard, Name = "侍卫", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "可挡一刀",
                SkillDesc = "",
                PassiveDesc = "被动：被攻击时可选择挡刀，代替被抽中的牌进入弃牌堆。" } },

            { CardType.Witch, new CardDef {
                Type = CardType.Witch, Name = "女巫", IsRole = true,
                SkillCost = 1, SubSkillCost = 1, SkillTag = "", PassiveTag = "",
                SkillDesc = "主动：毒药=额外攻击一次（1蓝）；解药=救活上一回合内阵亡的角色牌（1蓝）。各限一次。",
                PassiveDesc = "" } },

            { CardType.Arsenal, new CardDef {
                Type = CardType.Arsenal, Name = "军火库", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可追加攻击", PassiveTag = "",
                SkillDesc = "主动：每回合可额外攻击一次。",
                PassiveDesc = "" } },

            { CardType.Hero, new CardDef {
                Type = CardType.Hero, Name = "英雄", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "永不离手",
                SkillDesc = "", PassiveDesc = "英雄牌永不离开手牌，被攻击/扣押/勒索打中时改为扣1点HP。" } },

            { CardType.ArmedGuard, new CardDef {
                Type = CardType.ArmedGuard, Name = "带刀侍卫", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "亡语反击光环",
                SkillDesc = "",
                PassiveDesc = "光环：只要带刀侍卫在你手牌中，你的侍卫、大臣、带刀侍卫和守卫常驻亡语“被丢弃时立即攻击1次”（无需发动、不耗蓝）。被沉默整套时失效；被典狱长扣押离手即失效。" } },

            { CardType.Minister, new CardDef {
                Type = CardType.Minister, Name = "大臣", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "贿赂结盟", PassiveTag = "",
                SkillDesc = "主动：交一张牌给一名玩家，对方到你下回合开始前不能攻击你。",
                PassiveDesc = "" } },

            { CardType.GrandMinister, new CardDef {
                Type = CardType.GrandMinister, Name = "军机大臣", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "勒索牌型", PassiveTag = "",
                SkillDesc = "主动（限定技，整局一次）：获得其他所有人手牌中某种类型的全部牌。指定【英雄】= 所有对手各扣1点HP。（1蓝）",
                PassiveDesc = "" } },

            { CardType.Warden, new CardDef {
                Type = CardType.Warden, Name = "守卫", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "护一张牌", PassiveTag = "",
                SkillDesc = "主动：给一张手牌施加【无法选取】直到你的下回合开始。期间该牌留在手牌，但不参与任何针对手牌的判定（攻击抽牌/挡刀/驱魔/审判/扣押/人质/勒索/贿赂/合成材料）。不能选英雄牌。",
                PassiveDesc = "" } },

            { CardType.Maid, new CardDef {
                Type = CardType.Maid, Name = "侍女", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "免疫军火库",
                SkillDesc = "",
                PassiveDesc = "被动：免疫军火库的追加攻击（普攻和毒药照常吃）。" } },

            { CardType.GreenApple, new CardDef {
                Type = CardType.GreenApple, Name = "青苹果", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "受伤倒数",
                SkillDesc = "",
                PassiveDesc = "被动：你受伤后开始2回合倒数（再次受伤不会打断），倒数结束自动吃掉它，回复1点HP。它被攻击抽中时不会进入弃牌堆，而是被攻击者抢走。【2026-10-23 退环境：不再进入标准牌库——升级奖励改为回血后替代其中期生态位；引擎机制保留，自定义牌库仍可使用】" } },

            { CardType.Terrorist, new CardDef {
                Type = CardType.Terrorist, Name = "恐怖分子", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "安放炸弹", PassiveTag = "",
                SkillDesc = "主动（1蓝）：安放一枚炸弹，2回合后爆炸——对每个敌手（以安放者的敌我划线）各发动1次【无来源随机攻击】：等同普攻，随机抽其1张手牌正常结算（可被挡刀/2生命身体/亡灵免疫抵挡，不记仇），恐怖分子自己同归于尽。倒数期间被警察/police station沉默＝炸弹被拆除且本回合冷却返还（蓝不返还）；恐怖分子阵亡则炸弹失效；被大臣拿去贿赂送人也不改目标。（2026-10-13 由亡语反击改版，同日再改为随机攻击）",
                PassiveDesc = "" } },

            { CardType.Priestess, new CardDef {
                Type = CardType.Priestess, Name = "女祭司", IsRole = true,
                SkillCost = 1, SubSkillCost = 1, SkillTag = "继承双药", PassiveTag = "牧师亡语",
                SkillDesc = "合成配方：女巫＋牧师（2026-10-23 由3合1降为2合1，青苹果移出配方）。主动（继承自合成材料中的女巫，各限1次）：【毒药】1行动点=额外攻击一次；【解药】1行动点=救活上一回合内阵亡的角色牌——只继承合成时女巫还没用掉的药（\"药如果没用掉才继承，用掉了就没了\"）。",
                PassiveDesc = "被动：拥有【牧师】同款亡语——生命耗尽进入弃牌堆时（挡刀消耗或被攻击抽中），从弃牌堆回收1张角色牌（不能回收牧师、女祭司和英雄）。自身共2点生命。" } },

            { CardType.Valkyrie, new CardDef {
                Type = CardType.Valkyrie, Name = "女武神", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "挡刀·2生命",
                SkillDesc = "",
                PassiveDesc = "被动：2点生命——挡刀或被攻击直接抽中均消耗1点生命，还活着就回到手牌；生命耗尽才进入弃牌堆。" } },

            { CardType.Paladin, new CardDef {
                Type = CardType.Paladin, Name = "圣骑士", IsRole = true,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "", PassiveTag = "挡刀·亡语",
                SkillDesc = "",
                PassiveDesc = "合成配方：侍卫＋牧师（2026-10-23 由3合1降为2合1）。被动：2点生命——挡刀或被攻击直接抽中均消耗1点生命，生命耗尽进入弃牌堆时触发【牧师】同款亡语（被攻击抽走也算）：从弃牌堆回收一张角色牌（不能回收牧师、圣骑士和英雄）。" } },

            { CardType.Jailer, new CardDef {
                Type = CardType.Jailer, Name = "典狱长", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "扣押", PassiveTag = "",
                SkillDesc = "主动（2回合冷却）：1行动点，从对方手牌中扣押1张牌到你手中作为人质并封印（技能+被动失效）；人质扣2回合——原拥有者的第2个回合结束归还并解除封印。",
                PassiveDesc = "" } },

            { CardType.Inquisitor, new CardDef {
                Type = CardType.Inquisitor, Name = "审判官", IsRole = true,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "审判", PassiveTag = "",
                SkillDesc = "合成配方：驱魔师＋侍女（2026-10-23 由3合1降为2合1，牧师移出配方——加强垫底合成牌）。主动：1行动点，随机揭示对手1张手牌；若是【亡灵】或【女巫】（异端），将其当场处决进入弃牌堆。揭示的牌型公开播报。",
                PassiveDesc = "" } },

            // ---- 法术牌（3代·法术迸发 2026-10-20）：释放后直接消耗；未学会前是纯白板 ----
            { CardType.Alchemy, new CardDef {
                Type = CardType.Alchemy, Name = "炼金术", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "【2026-10-23 退环境：不再进入3代标准牌库（法术槽位有限）】。原效果：法术（1行动点）：通用法术——释放时选择一种你已研习的法术，当场以该法术的效果结算。",
                PassiveDesc = "" } },
            { CardType.Clarity, new CardDef {
                Type = CardType.Clarity, Name = "清晰术", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "【2026-10-23 退环境：不再进入3代标准牌库】；其「标记」机制由【点燃】继承——被标记的牌无法被随机选取、但可被定向攻击。",
                PassiveDesc = "" } },
            { CardType.Ignite, new CardDef {
                Type = CardType.Ignite, Name = "点燃", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "法术（1行动点，2026-10-23 重做）：查看对手2张手牌（随机亮出，不足全亮），选择其中1张点燃——被点牌获得2回合【标记】（无法被随机选取、可被定向攻击），2回合后受到1点伤害（走完整结算：经验/亡语/淘汰照常）；若选中的是【军火库】或【英雄】牌，则立刻造成1点伤害、不产生标记。释放后法术牌消耗掉。",
                PassiveDesc = "" } },
            { CardType.Thunder, new CardDef {
                Type = CardType.Thunder, Name = "雷霆", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "法术（1行动点）：对选中的玩家连续攻击，攻击次数=其当前手牌数的25%（四舍五入），逐次随机抽牌正常结算。释放后法术牌消耗掉。",
                PassiveDesc = "" } },
            { CardType.Comet, new CardDef {
                Type = CardType.Comet, Name = "彗星", IsRole = false,
                SkillCost = 0, SubSkillCost = -1, SkillTag = "可反制", PassiveTag = "法术",
                SkillDesc = "法术（不耗行动点）：当有人释放法术时，你可以消耗1张【彗星】抵消那个法术（被抵消的法术直接消耗、效果不结算）。需先研习（learn）获得反制权限。",
                PassiveDesc = "被动（反制）：有人释放法术时，可消耗本牌抵消之，不耗行动点。未研习前不可反制。" } },
            { CardType.Barrier, new CardDef {
                Type = CardType.Barrier, Name = "风墙", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "法术（1行动点，2026-10-23 由「屏障」更名重做）：选择一名玩家——获得持续2回合的2点【定向护盾】：只能吸收来自该玩家的伤害（普攻/军火库/毒药/强攻追加/勒索直伤均可被吸收；其余来源的伤害照常结算）。释放后法术牌消耗掉。",
                PassiveDesc = "" } },
            { CardType.Glacier, new CardDef {
                Type = CardType.Glacier, Name = "冰川", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "法术（1行动点，2026-10-23 新增）：选择一名对手——其下2个回合合并为1个：回合照常轮到它（抽牌/倒数/联盟扣减等照常），但行动点不回满，所有每回合限1次的行动（普通攻击/警察/驱魔等）也不刷新；它可自主分配这两回合的行动点（如前3后0、前2后1、前0后3）。不能对已被冰川冻结的目标叠加释放。释放后法术牌消耗掉。",
                PassiveDesc = "" } },
            { CardType.Heal, new CardDef {
                Type = CardType.Heal, Name = "治疗术", IsRole = false,
                SkillCost = 1, SubSkillCost = -1, SkillTag = "可释放", PassiveTag = "法术",
                SkillDesc = "法术（1行动点，2026-10-23 新增）：立刻为你的英雄回复1点HP（满血时无效果）。替代青苹果的中期回血生态位，与等级系统兼容——偏后期瞬发回复手段。释放后法术牌消耗掉。",
                PassiveDesc = "" } },
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
